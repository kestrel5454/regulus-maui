const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../Platforms/Android/ScheduleScriptClient.cs'), 'utf8');
const script = source.match(/const string VisitPersonScript = """\r?\n([\s\S]*?)\r?\n""";/)[1];
const storage = new Map();
function start({ unavailable = false } = {}) {
  const change = [];
  const context = {
    localStorage: {
      getItem(key) { if (unavailable) throw Error('Unavailable'); return storage.get(key); },
      setItem(key, value) { if (unavailable) throw Error('Unavailable'); storage.set(key, value); }
    },
    visitPerson: { value: '', options: [], addEventListener: (_, fn) => change.push(fn) },
    fillVisitPeople(names) {
      const previous = context.visitPerson.value;
      context.visitPerson.options = names.map(value => ({ value }));
      context.visitPerson.value = names.includes(previous) ? previous : names[0] || '';
    }
  };
  vm.createContext(context);
  vm.runInContext(script, context);
  context.select = name => { context.visitPerson.value = name; change.forEach(fn => fn()); };
  return context;
}
let app = start();
app.fillVisitPeople(['薮田', '松井']);
assert.equal(app.visitPerson.value, '薮田', 'First use keeps the existing default');
app.select('松井');
app = start();
app.fillVisitPeople(['薮田', '松井']);
assert.equal(app.visitPerson.value, '松井', 'Selection survives a new app context');
app.fillVisitPeople(['薮田']);
assert.equal(app.visitPerson.value, '薮田', 'Missing person falls back to an available person');
app.fillVisitPeople([]);
app.select('');
app = start();
app.fillVisitPeople(['薮田', '松井']);
assert.equal(app.visitPerson.value, '松井', 'Fallback and empty days must not overwrite the saved person');
app.select('薮田');
app = start();
app.fillVisitPeople(['松井', '薮田']);
assert.equal(app.visitPerson.value, '薮田', 'An explicit change replaces the saved selection');
app = start({ unavailable: true });
app.fillVisitPeople(['薮田', '松井']);
app.select('松井');
app.fillVisitPeople(['薮田', '松井']);
assert.equal(app.visitPerson.value, '松井', 'Storage failure still allows in-memory selection');
console.log('Visit person persistence: restart, changes, missing people, empty days, and unavailable storage passed.');
