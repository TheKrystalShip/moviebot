// A whole answer filtered and faceted where the page holds it: any of a section's picks, all of the
// sections, and each section counted without its own picks.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { filterWhole, wholeFacets } from '../src/facets.js';
import { queryOf } from '../src/query.js';

const film = (title, year, genres, minutes, rating, language, category = 'general') =>
  ({ kind: 'film', title, year, genres, runtimeMinutes: minutes, rating, originalLanguage: language, category });

const wants = [
  film('Night', 1994, ['Horror'], 95, 6.9, 'en'),
  film('Gold', 2015, ['Adventure'], 160, 7.9, 'fr'),
  film('Hollow', 2018, ['Horror', 'Crime'], 100, 8.3, 'ko'),
  film('Song', 2023, ['Adventure'], 80, 7.1, 'en'),
  film('Toon', 2011, ['Animation'], 90, 7.7, 'ja', 'anime'),
  { kind: 'show', title: 'Tales', year: 2020, genres: ['Drama'], rating: 7.5, originalLanguage: 'en', category: 'general' },
];

const options = (facets, id) => facets.find((s) => s.id === id).options.map((o) => o.value);

test('two genres keep titles of either, and a second section narrows them', () => {
  assert.deepEqual(filterWhole(wants, { genre: ['Horror', 'Adventure'] }).map((t) => t.title), ['Night', 'Gold', 'Hollow', 'Song']);
  assert.deepEqual(filterWhole(wants, { genre: ['Horror', 'Adventure'], decade: ['2010'] }).map((t) => t.title), ['Gold', 'Hollow']);
});

test('a section is counted without its own picks and with every other', () => {
  const facets = wholeFacets(wants, { genre: ['Adventure'] });
  assert.deepEqual(options(facets, 'genre'), ['Adventure', 'Animation', 'Crime', 'Drama', 'Horror']);
  assert.deepEqual(options(facets, 'decade'), ['2020', '2010']);
  assert.deepEqual(options(facets, 'length'), ['short', 'long']);
  assert.deepEqual(facets.find((s) => s.id === 'decade').options.map((o) => o.count), [1, 1]);
});

test('kinds are films, shows and anime, and a fixed section is not reported', () => {
  const facets = wholeFacets(wants, {}, ['genre']);
  assert.deepEqual(options(facets, 'kind'), ['film', 'show', 'anime']);
  assert.equal(facets.find((s) => s.id === 'genre'), undefined);
  assert.deepEqual(options(facets, 'rating'), ['8', '7', '6']);
});

test('a query repeats a list key once per value and leaves out what is empty', () => {
  assert.equal(queryOf({ kind: 'film', decade: [1990, 2020], anyGenre: [], language: null, q: 'a b' }), 'kind=film&decade=1990&decade=2020&q=a+b');
});
