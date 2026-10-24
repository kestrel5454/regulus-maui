const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Execute the production bridge helpers, without a WebView or network.
const source = fs.readFileSync(path.join(__dirname, '../../Pages/RegulusPage.xaml.cs'), 'utf8');
new vm.Script(source.match(/const string BridgeScript = """\r?\n([\s\S]*?)\r?\n""";/)[1]);
const helpers = source.slice(source.indexOf('function textOf('), source.indexOf('function hideVisit('));
const sent = [];
const context = { window: { __regulusEmergency: false }, RegulusNative: { postReception: body => sent.push(body) } };
context.window.RegulusNative = context.RegulusNative;
vm.createContext(context);
vm.runInContext(helpers, context);
const comments = (...bodies) => ({ querySelectorAll: selector => {
  assert.equal(selector, '.comment-item .comment-body');
  return bodies.map(textContent => ({ textContent }));
} });

context.post('v', '受付番号：12345678', comments('受付番号：12345678', 'お客様名：試験'));
assert.equal(sent.pop(), 'v:12345678', 'Off uses the existing number-only payload');
context.post('s', '番号なし');
assert.equal(sent.pop(), 'none');
context.post('s', '受付番号：12345678 受付番号：87654321');
assert.equal(sent.pop(), 'ambiguous');

context.window.__regulusEmergency = true;
context.post('v', 'ignored merged comments', comments('受付番号：12345678', 'お客様名：試験 ID：12345678', '別の顧客'));
assert.deepEqual(JSON.parse(sent.pop().slice('emergency:'.length)), {
  number: '12345678', text: 'お客様名：試験 ID：12345678'
}, 'Only the second body is sent; no author, timestamp or third comment');
context.post('v', 'stale receipt', comments('受付番号：12345678'));
assert.equal(JSON.parse(sent.pop().slice('emergency:'.length)).text, '', 'No fallback to another comment');
context.post('s', 'stale receipt', null);
assert.deepEqual(JSON.parse(sent.pop().slice('emergency:'.length)), { number: '', text: '' });
context.post('v', 'merged', comments('受付番号なし', 'お客様名：試験 ID：12345678'));
assert.deepEqual(JSON.parse(sent.pop().slice('emergency:'.length)), {
  number: '', text: 'お客様名：試験 ID：12345678'
}, 'The second comment ID can supply the reception number when the first has none');
context.window.__regulusEmergency = false;
context.post('s', '受付番号：87654321');
assert.equal(sent.pop(), 's:87654321', 'Turning off restores the original protocol');
console.log('PASS: emergency bridge switching and second-comment isolation (8 cases)');
