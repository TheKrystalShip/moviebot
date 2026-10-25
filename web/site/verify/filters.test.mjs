// Discover's filter: the picks the address holds, what the API is asked with them, and the drawer's
// sections from an answer's facets.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { apiPicks, filterQuestion, pickCount, picksOfValues, readPicks, sectionsOf, withPicks, drawerValues } from '../src/filters.js';

const none = { kind: null, genre: [], decade: [], length: [], rating: null, language: [] };

test('the address holds several values per section and keeps every other parameter', () => {
  const picks = readPicks(new URLSearchParams('shelf=date-night&k=show&g=Horror&g=Comedy&decade=1990,2010&rating=7&lang=ko&lang=xx1'));
  assert.deepEqual(picks, { kind: 'show', genre: ['Horror', 'Comedy'], decade: ['1990', '2010'], length: [], rating: '7', language: ['ko'] });
  assert.equal(pickCount(picks), 7);
  const path = withPicks(new URLSearchParams('shelf=date-night&g=Drama&sort=rating'), picks);
  const back = new URLSearchParams(path.split('?')[1]);
  assert.equal(back.get('shelf'), 'date-night');
  assert.equal(back.get('sort'), 'rating');
  assert.deepEqual(back.getAll('g'), ['Horror', 'Comedy']);
  assert.deepEqual(readPicks(back), picks);
});

test('the API is asked by its own names: anyGenre, and anime as every kind in the anime category', () => {
  assert.deepEqual(apiPicks({ ...none, kind: 'anime', genre: ['Drama'], rating: '8' }),
    { kind: 'all', category: 'anime', anyGenre: ['Drama'], minRating: '8' });
  assert.deepEqual(apiPicks(none), {});
});

test("the catalogue's floor of votes is the same with a rating picked or not", () => {
  assert.equal(filterQuestion(none).minVotes, filterQuestion({ ...none, rating: '8' }).minVotes);
  assert.equal(filterQuestion(none).kind, 'all');
  assert.equal(filterQuestion(none, 'newest').sort, 'newest');
});

test('sections are the facets with two options or more, each pick kept to be taken off', () => {
  const facets = { sections: [
    { id: 'kind', options: [{ value: 'film' }, { value: 'show' }] },
    { id: 'genre', options: [{ value: 'Horror' }, { value: 'Drama' }] },
    { id: 'decade', options: [{ value: '2020' }] },
    { id: 'language', options: [{ value: 'en', label: 'English' }] },
  ] };
  const picks = { ...none, decade: ['1990'] };
  const sections = sectionsOf(facets, picks, { watched: true });
  assert.deepEqual(sections.map((s) => s.id), ['kind', 'genre', 'decade', 'watched']);
  assert.deepEqual(sections[0].options.map((o) => o.label), ['All', 'Movies', 'TV shows']);
  assert.deepEqual(sections[2].options.map((o) => o.label), ['2020s', '1990s']);
  assert.deepEqual(sectionsOf(null, picks, { watched: false }).map((s) => s.id), ['decade']);
});

test('an empty option is drawn and cannot be picked, and a picked one can always be taken off', () => {
  const facets = { sections: [{ id: 'genre', options: [{ value: 'Horror' }, { value: 'Drama', empty: true }, { value: 'Crime', empty: true }] }] };
  const genre = sectionsOf(facets, { ...none, genre: ['Crime'] }, { watched: false }).find((s) => s.id === 'genre');
  assert.deepEqual(genre.options.map((o) => [o.value, o.disabled]), [['Horror', false], ['Drama', true], ['Crime', true]]);
});

test("the drawer's values come back as the same picks", () => {
  const picks = { ...none, genre: ['Horror'], rating: '6' };
  assert.deepEqual(picksOfValues(drawerValues(picks, true)), picks);
  assert.equal(picksOfValues({ ...drawerValues(picks, false), kind: 'all' }).kind, null);
});

test("a page's address keeps its own path and its own parameters beside the picks", () => {
  const picks = { kind: null, genre: ['Drama'], decade: [], length: [], rating: null, language: [] };
  const path = withPicks(new URLSearchParams('state=ready&g=Horror'), picks, '/movies', { state: null, downloaded: true });
  assert.equal(path, '/movies?g=Drama&downloaded=1');
  assert.equal(withPicks(new URLSearchParams(''), { ...picks, genre: [] }, '/wants'), '/wants');
});

test('a language the facets name by code alone is named in English', () => {
  const sections = sectionsOf({ sections: [{ id: 'language', options: [{ value: 'ko' }, { value: 'fr' }] }] }, { kind: null, genre: [], decade: [], length: [], rating: null, language: [] }, { watched: false });
  assert.deepEqual(sections.find((s) => s.id === 'language').options.map((o) => o.label), ['Korean', 'French']);
});
