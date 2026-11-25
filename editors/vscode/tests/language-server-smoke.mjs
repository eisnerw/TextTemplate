import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const extensionRoot = process.cwd();
const repositoryRoot = path.resolve(extensionRoot, '../..');
const serverPath = path.join(extensionRoot, 'server', 'dist', 'TextTemplate.LanguageServer.dll');
const processHandle = spawn('dotnet', [serverPath], { stdio: ['pipe', 'pipe', 'pipe'] });
let stdout = Buffer.alloc(0);
let stderr = '';
const queuedMessages = [];
const pendingReaders = [];

processHandle.stderr.setEncoding('utf8');
processHandle.stderr.on('data', chunk => { stderr += chunk; });
processHandle.stdout.on('data', chunk => {
	stdout = Buffer.concat([stdout, chunk]);
	try {
		readMessages();
	} catch (error) {
		for (const reader of pendingReaders.splice(0)) {
			clearTimeout(reader.timer);
			reader.reject(error);
		}
	}
});

function readMessages() {
	while (true) {
		const separator = stdout.indexOf('\r\n\r\n');
		if (separator < 0) return;
		const header = stdout.subarray(0, separator).toString('ascii');
		const length = Number(header.match(/Content-Length:\s*(\d+)/i)?.[1]);
		if (!Number.isInteger(length)) throw new Error(`Invalid LSP header: ${header}`);
		const bodyStart = separator + 4;
		if (stdout.length < bodyStart + length) return;
		const message = JSON.parse(stdout.subarray(bodyStart, bodyStart + length).toString('utf8'));
		stdout = stdout.subarray(bodyStart + length);
		const reader = pendingReaders.shift();
		if (reader) {
			clearTimeout(reader.timer);
			reader.resolve(message);
		} else {
			queuedMessages.push(message);
		}
	}
}

function nextMessage() {
	if (queuedMessages.length) return Promise.resolve(queuedMessages.shift());
	return new Promise((resolve, reject) => {
		const reader = { resolve, reject, timer: undefined };
		reader.timer = setTimeout(() => {
			const index = pendingReaders.indexOf(reader);
			if (index >= 0) pendingReaders.splice(index, 1);
			reject(new Error(`Timed out waiting for language server output. stderr: ${stderr}`));
		}, 5000);
		pendingReaders.push(reader);
	});
}

function send(message) {
	const body = Buffer.from(JSON.stringify(message), 'utf8');
	const header = Buffer.from(`Content-Length: ${body.length}\r\n\r\n`, 'ascii');
	processHandle.stdin.write(Buffer.concat([header, body]));
}

try {
	send({ jsonrpc: '2.0', id: 1, method: 'initialize', params: { rootUri: pathToFileURL(repositoryRoot).href } });
	const initialized = await nextMessage();
	assert.equal(initialized.id, 1);
	assert.deepEqual(initialized.result.capabilities.textDocumentSync, { openClose: true, change: 1 });
	send({ jsonrpc: '2.0', method: 'initialized', params: {} });

	const documentUri = pathToFileURL(path.join(repositoryRoot, 'templates', 'sample.template')).href;
	send({
		jsonrpc: '2.0',
		method: 'textDocument/didOpen',
		params: { textDocument: { uri: documentUri, languageId: 'texttemplate', version: 1, text: 'Hello {name' } }
	});
	const invalidResult = await nextMessage();
	assert.equal(invalidResult.method, 'textDocument/publishDiagnostics');
	assert.equal(invalidResult.params.uri, documentUri);
	assert.equal(invalidResult.params.diagnostics.length, 1);
	assert.equal(invalidResult.params.diagnostics[0].source, 'TextTemplate');
	assert.match(invalidResult.params.diagnostics[0].message, /Expected RBRACE/);

	send({
		jsonrpc: '2.0',
		method: 'textDocument/didChange',
		params: { textDocument: { uri: documentUri, version: 2 }, contentChanges: [{ text: 'Hello {name}' }] }
	});
	const validResult = await nextMessage();
	assert.equal(validResult.params.diagnostics.length, 0);

	send({ jsonrpc: '2.0', id: 2, method: 'shutdown', params: null });
	assert.equal((await nextMessage()).id, 2);
	send({ jsonrpc: '2.0', method: 'exit', params: null });
	const exitCode = await new Promise((resolve, reject) => {
		processHandle.once('error', reject);
		processHandle.once('exit', resolve);
	});
	assert.equal(exitCode, 0, stderr);
	console.log('Language-server smoke test passed.');
} finally {
	if (processHandle.exitCode === null) processHandle.kill();
}