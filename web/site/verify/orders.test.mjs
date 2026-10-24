// A page's orders in its filter drawer: the sort section, how a change is split between the order and
// the filter, and the count under the page's title.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { ORDER, orderSection, shownOf, withOrder } from '../src/orders.js';

const orders = [
  { key: 'added', dir: 'desc', label: 'Recently added' },
  { key: 'added', dir: 'asc', label: 'First added' },
  { key: 'rating', dir: 'desc', label: 'Highest rated' },
];

test('the sort section offers each order by its words, the first being where the page opens', () => {
  const section = orderSection(orders);
  assert.equal(section.kind, 'sort');
  assert.equal(section.base, 'added:desc');
  assert.deepEqual(section.options.map((o) => o.label), ['Recently added', 'First added', 'Highest rated']);
});

test('a picked order goes to the order and every other change to the filter', () => {
  const picked = [];
  const filtered = [];
  const field = withOrder({ sections: [{ id: 'genre' }], values: { genre: [] }, onValues: (v) => filtered.push(v) },
    orders, { key: 'added', dir: 'desc' }, (key, dir) => picked.push([key, dir]));

  assert.deepEqual(field.sections.map((s) => s.id), [ORDER, 'genre']);
  assert.equal(field.values[ORDER], 'added:desc');

  field.onValues({ ...field.values, [ORDER]: 'rating:desc' });
  assert.deepEqual(picked, [['rating', 'desc']]);
  assert.equal(filtered.length, 0);

  field.onValues({ ...field.values, genre: ['drama'] });
  assert.deepEqual(filtered, [{ genre: ['drama'] }]);
});

test('the count names how many are shown only while some are not', () => {
  assert.equal(shownOf(248, 248, ['movie', 'movies']), '248 movies');
  assert.equal(shownOf(25, 248, ['movie', 'movies']), '25 of 248 movies');
  assert.equal(shownOf(1, 1, ['movie', 'movies']), '1 movie');
});
