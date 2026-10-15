const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

// The C# regression runner exports the exact production JavaScript constant.
const script = fs.readFileSync(path.join(__dirname, 'bin/Debug/net10.0/document-session.js'), 'utf8');
function read({ text = '', password = false, hiddenPassword = false, labels = [], results = false, body = true } = {}) {
  const document = {
    body: body ? { innerText: text } : null,
    getElementById: () => results ? {} : null,
    querySelectorAll(selector) {
      if (selector.includes('password')) return password || hiddenPassword
        ? [{ offsetParent: password ? {} : null, getClientRects: () => password ? [{}] : [] }] : [];
      return labels.map(textContent => ({ textContent }));
    }
  };
  return vm.runInNewContext(script, { document });
}

assert.equal(read({ password: true }), 'login', 'Detect a login form at a customer URL');
assert.equal(read({ text: 'セッションの有効期限が切れました。' }), 'login');
assert.equal(read({ text: 'ログインしてください' }), 'login');
assert.equal(read({ text: '再度 ログイン してください' }), 'login');
assert.equal(read({ text: '顧客情報', hiddenPassword: true }), 'in', 'Ignore hidden password inputs');
assert.equal(read({ text: 'ログインしてくださいとご案内', labels: ['お客様名'] }), 'in', 'Customer notes are not expiry messages');
assert.equal(read({ results: true }), 'in', 'Empty search results remain valid');
assert.equal(read({ body: false }), 'unknown', 'A missing document is not authenticated');
console.log('PASS: document session detection (8 cases)');
