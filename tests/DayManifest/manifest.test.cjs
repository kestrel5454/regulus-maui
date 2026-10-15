const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

// Execute the actual module bridge injected into the scheduler, without network or personal data.
const source = fs.readFileSync(path.join(__dirname, '../../Platforms/Android/ScheduleScriptClient.cs'), 'utf8');
const script = source.match(/const string DayManifestScript = """\r?\n([\s\S]*?)\r?\n""";/)[1];
const cell = (text, comments) => ({ text, comments });
const person = (...cells) => ({ cells: [null, null, ...cells] });
let parsed;
let result;
let reads = 0;
const context = {
  window: { RegulusNative: { postReception: payload => { result = JSON.parse(payload.slice(4)); } } },
  activeUser: {},
  readScheduleDay: async () => { reads++; return parsed; },
  parseSchedule: data => data,
  cellText: value => value?.text || '',
  parseComments: comments => [{ text: comments || '' }]
};
vm.createContext(context);
vm.runInContext(script, context);

(async () => {
  parsed = { people: [
    person(cell('A', '受付番号：12345678'), cell('B', '受付番号:12345678'), cell('', ''), cell('C', '87654321'), cell('予備', '11223344')),
    person(cell('D', '55667788'), cell('番号なし', ''), cell('曖昧', '12345678 87654321'))
  ] };
  await context.window.__regulusReadDay('2026-10-15', 7);
  assert.deepEqual(result, { token: 7, date: '2026-10-15', numbers: ['12345678', '87654321', '11223344', '55667788'], missing: 2 });
  parsed = { people: [] };
  await context.window.__regulusReadDay('2026-10-16', 8);
  assert.deepEqual(result.numbers, []);
  assert.equal(result.missing, 0);
  parsed = null;
  await context.window.__regulusReadDay('2026-10-17', 9);
  assert.equal(result.error, true);
  const before = reads;
  context.activeUser = null;
  await context.window.__regulusReadDay('2026-10-15', 10);
  assert.equal(result.error, true);
  assert.equal(reads, before);
  context.activeUser = {};
  await context.window.__regulusReadDay('invalid', 11);
  assert.equal(result.error, true);
  assert.equal(reads, before);
  console.log('Day manifest: all cases passed.');
})().catch(error => { console.error(error); process.exitCode = 1; });
