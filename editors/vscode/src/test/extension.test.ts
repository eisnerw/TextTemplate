import * as assert from 'assert';

import * as vscode from 'vscode';

suite('Extension Test Suite', () => {
	test('registers the TextTemplate language id', async () => {
		const languages = await vscode.languages.getLanguages();
		assert.ok(languages.includes('texttemplate'));
	});
});
