// Test-only parser oracle. Uses the original generated JavaScript grammar, never the C# parser.
import fs from 'node:fs';
import antlr4 from 'antlr4';
import Lexer from './.build/src/main-generated/javascript/TextTemplateLexer.js';
import Parser from './.build/src/main-generated/javascript/TextTemplateParser.js';

const node = (kind, value = null, children = []) => ({ kind, value, children });
const rules = ctx => (ctx.children || []).filter(c => c.ruleIndex !== undefined);
const name = ctx => ctx.constructor.name.replace(/Context$/, '');
const translated = ctx => rules(ctx).flatMap(convert);
function convert(ctx) {
  const children = () => translated(ctx);
  switch (name(ctx)) {
    case 'CompilationUnit': return [node('Template', null, children())];
    case 'SubtemplateSection': return [node('SubtemplateSection', null, children())];
    case 'Text':
    case 'Continuation':
    case 'Bullet':
    case 'BeginningBullet':
      return (ctx.children || []).flatMap(c => {
        if (c.ruleIndex !== undefined) return convert(c);
        const kind = {TEXT:'Text', SPACES:'Whitespace', NL:'NewLine', CONTINUATION:'Continuation', BULLET:'Bullet'}[Parser.symbolicNames[c.symbol.type]];
        if (!kind) throw new Error('Unexpected text token ' + c.getText());
        return [node(kind, c.getText())];
      });
    case 'TemplateToken': return [node('Interpolation', null, children())];
    case 'ContextToken': return [node('Context', ctx.children[0].getText() === ':' ? 'implicit' : 'explicit', children())];
    case 'TemplateContextToken': return children();
    case 'TemplateContextCommaToken': {
      const all = children(); const context = all.shift();
      return [node('ContextWithFallback', context.value, [...context.children, ...all])];
    }
    case 'Identifier':
    case 'IdentifierOperand':
    case 'IdentifierCondition': return [node('Identifier', ctx.getText())];
    case 'NamedSubtemplate': return [node('NamedSubtemplate', ctx.getText().slice(1))];
    case 'Digits': return [node('Number', ctx.getText())];
    case 'Regex': return [node('Regex', ctx.getText())];
    case 'QuoteLiteral':
    case 'ApostropheLiteral':
    case 'QuoteOperand':
    case 'ApostropheOperand': return [node('String', ctx.getText())];
    case 'BracketedTemplateSpec':
    case 'InvokedTemplateSpec': return [node('InlineTemplate', null, children())];
    case 'MethodInvoked': {
      const parts = rules(ctx); let value = convert(parts.shift())[0];
      for (const invocation of parts) {
        const invocationParts = rules(invocation);
        const method = invocationParts.shift();
        const args = invocationParts.flatMap(convert);
        value = name(method) === 'Method'
          ? node('Call', method.getText().slice(1, -1), [value, ...args])
          : node('InlineCall', null, [value, ...convert(method), ...args]);
      }
      return [value];
    }
    case 'Arguments': return [node('Arguments', null, children())];
    case 'NestedPredicate': return [node('Group', null, children())];
    case 'NotPredicate': return [node('Not', '!', children())];
    case 'LogicalOperator': return [node('Logical', ctx.children[1].getText(), children())];
    case 'RelationalOperation': return [node('Relational', ctx.children[1].getText(), children())];
    case 'BracedArrow': return [node('Arrow', '=>', children())];
    case 'BracedThinArrow': return [node('ThinArrow', '->', children())];
    case 'TemplateContents': case 'SubtemplateSpecs': case 'SubtemplateSpec':
    case 'BraceArrow': case 'BraceThinArrow': case 'Braced': case 'BracedPredicate':
    case 'Condition': case 'RelationalOperand': case 'TemplateSpec': case 'BracedArrowTemplateSpec':
    case 'MethodableTemplateSpec': case 'OptionallyInvoked': case 'DigitsArgument':
    case 'PredicateArgument': case 'OptionallyInvokedArgument': return children();
    default: throw new Error('Unhandled ANTLR context ' + name(ctx));
  }
}

const output = {};
for (const [key, input] of Object.entries(JSON.parse(fs.readFileSync(0, 'utf8')))) {
  const errors = [];
  const text = input.text.replace(/\r/g, '').replace(/\t/g, '    ');
  const shared = input.category === 'subtemplates';
  // Same wrapper used when the interpreter invokes a shared subtemplate.
  const wrapped = shared ? '{:' + text + '}' : text;
  const lexer = new Lexer(new antlr4.InputStream(wrapped));
  const listener = { syntaxError: (_r, _s, line, column, message) => errors.push({ line, column, message }),
    reportAmbiguity() {}, reportAttemptingFullContext() {}, reportContextSensitivity() {} };
  lexer.removeErrorListeners(); lexer.addErrorListener(listener);
  const stream = new antlr4.CommonTokenStream(lexer);
  const parser = new Parser(stream);
  parser.removeErrorListeners(); parser.addErrorListener(listener);
  const parsed = parser.compilationUnit();
  stream.fill();
  const tokens = stream.tokens.filter(t => t.type !== -1 && (!shared || (t.start >= 2 && t.stop < wrapped.length - 1)))
    .map(t => ({ kind: parser.symbolicNames[t.type], text: t.text }));
  let tree = null;
  if (!errors.length) {
    tree = convert(parsed)[0];
    if (shared) tree = node('SharedTemplate', null, tree.children[0].children);
  }
  output[key] = { errors, tokens, tree };
}
process.stdout.write(JSON.stringify(output));
