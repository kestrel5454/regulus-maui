const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../Platforms/Android/ScheduleScriptClient.cs'), 'utf8');
const script = source.match(/const string DayManifestScript = """\r?\n([\s\S]*?)\r?\n""";/)[1];
class Element {
  constructor() { this.children = []; this.style = {}; this.events = {}; }
  append(...nodes) { nodes.forEach(node => this.appendChild(node)); }
  appendChild(node) { if (node.parentElement) node.remove(); node.parentElement = this; this.children.push(node); }
  insertBefore(node, before) { node.parentElement = this; this.children.splice(this.children.indexOf(before), 0, node); }
  setAttribute() {}
  addEventListener(type, fn) { (this.events[type] ||= []).push(fn); }
  querySelector(selector) { return this.children.find(node => '.' + node.className === selector); }
  remove() { this.parentElement.children.splice(this.parentElement.children.indexOf(this), 1); }
}
const cell = (text, comments) => ({ text, comments });
const person = (name, ...cells) => ({ name, cells: [null, null, ...cells] });
let result;
const cards = new Element();
const view = new Element(); view.appendChild(cards);
const context = {
  window: { RegulusNative: { postReception: payload => { result = payload.startsWith('day:') ? JSON.parse(payload.slice(4)) : payload; } } },
  document: { createElement: () => new Element() },
  activeUser: {}, mobileTab: 'visit', visitLoadToken: 1,
  visitDate: Object.assign(new Element(), { value: '2026-10-15' }),
  visitPerson: Object.assign(new Element(), { value: 'A' }),
  visitCards: cards,
  VISIT_SLOTS: [2, 3, 4, 5].map(offset => ({offset})),
  visitScheduleData: { people: [
    person('A', cell('A1', '受付番号：12345678'), cell('A2', '12345678'), cell('曖昧', '12345678 87654321'), cell('', ''), cell('予備', '11223344')),
    person('B', cell('B1', '87654321'))
  ] },
  cellText: value => value?.text || '',
  parseComments: comments => [{text: comments || ''}],
};
context.selectedVisitPerson = data => data.people.find(person => person.name === context.visitPerson.value);
context.renderVisitCards = () => { cards.children = context.VISIT_SLOTS.map(() => new Element()); };
context.window.__regulusScheduleDate = '2026-10-15';
context.window.__regulusScheduleToken = 1;
const filters = new Element();
const personLabel = new Element();
personLabel.appendChild(context.visitPerson);
filters.appendChild(personLabel);
vm.createContext(context);
vm.runInContext(script, context);
const personRow = filters.children[0];
assert.equal(personRow.id, 'regulus-person-actions');
assert.equal(personRow.children[0], personLabel);
assert.equal(personRow.children[1].textContent, '1日分取得');
assert.ok(personRow.style.cssText.includes('minmax(0,1fr) minmax(0,1fr)'), 'Person and fetch action share equal columns');
const read = token => context.window.__regulusReadDay(token);
read(1); assert.equal(result.error, true, 'Must wait until cards have rendered');
context.renderVisitCards();
read(2);
assert.deepEqual(result, {token: 2, date: '2026-10-15', person: 'A', numbers: ['12345678'], missing: 1});
assert.equal(view.children[0].id, 'regulus-day-cache', 'Toolbar belongs inside the card view');
context.window.__regulusSetDayCacheState({numbers: ['12345678'], date: '2026-10-15', person: 'A', message: '取得済み 1/1件', running: false});
assert.equal(cards.children[0].querySelector('.regulus-cached').textContent, '✓ 取得済み');
assert.ok(cards.children[1].querySelector('.regulus-cached'), 'Duplicate reception numbers share cached state');
assert.equal(cards.children[2].querySelector('.regulus-cached'), undefined);
assert.equal(view.children[0].children[0].textContent, '取得済み 1/1件');
context.visitPerson.value = 'B'; context.renderVisitCards(); read(3);
assert.deepEqual(result.numbers, ['87654321']);
assert.equal(cards.children[0].querySelector('.regulus-cached'), undefined);
assert.ok(!view.children[0].children[0].textContent.includes('1/1'), 'Progress must not leak across people');
context.visitDate.value = '2026-10-16'; context.visitLoadToken++;
read(4); assert.equal(result.error, true, 'Old cards must not be fetched while a new day loads');
context.renderVisitCards(); read(5); assert.equal(result.error, true, 'Changing person during a day load must not relabel stale data');
context.window.__regulusScheduleDate = '2026-10-16'; context.window.__regulusScheduleToken = 2;
context.renderVisitCards(); read(6); assert.equal(result.date, '2026-10-16');
context.mobileTab = 'schedule'; read(7); assert.equal(result.error, true);
context.mobileTab = 'visit'; context.activeUser = null; read(8); assert.equal(result.error, true);
console.log('Card day retrieval: selected person/slots, stale load protection, badges, and progress passed.');
