import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vscode from 'vscode';
import { LanguageClient, TransportKind, type LanguageClientOptions, type ServerOptions } from 'vscode-languageclient/node';

let client: LanguageClient | undefined;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
	const serverModule = context.asAbsolutePath(path.join('server', 'dist', 'TextTemplate.LanguageServer.dll'));
	if (!fs.existsSync(serverModule)) {
		void vscode.window.showErrorMessage('TextTemplate language server is not built. Run "npm run compile" in the extension folder.');
		return;
	}

	const serverOptions: ServerOptions = {
		run: { command: 'dotnet', args: [serverModule], transport: TransportKind.stdio },
		debug: { command: 'dotnet', args: [serverModule], transport: TransportKind.stdio }
	};
	const clientOptions: LanguageClientOptions = {
		documentSelector: [
			{ scheme: 'file', language: 'texttemplate' },
			{ scheme: 'untitled', language: 'texttemplate' }
		]
	};

	client = new LanguageClient('texttemplate', 'TextTemplate Language Server', serverOptions, clientOptions);
	context.subscriptions.push(client);
	await client.start();
}

export async function deactivate(): Promise<void> {
	await client?.stop();
	client = undefined;
}
