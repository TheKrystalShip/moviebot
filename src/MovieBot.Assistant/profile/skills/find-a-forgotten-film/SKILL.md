---
name: find-a-forgotten-film
description: Identify a film or show someone half remembers from a scene, a trailer, an actor, a plot detail or roughly when they saw it.
---

# Find a forgotten film

1. Extract the clues from what the person said, one by one: people, years, whether it is out yet, look (black and white, animated), plot words, language. Turn every clue about when into years from today's date in the reminder: "recently announced" is this year or the year before, with released set to upcoming or announced; "last summer" is last year. Do not add a clue they did not give.
2. Call find_by_clues once with every clue. It searches the database and ranks the candidates by how many clues each fits, and says which it fits and which it does not.
3. web_search only for a clue find_by_clues could not use: a scene, a trailer, a rumour, news. Search in the words a review or a news story would use, then look the title up with find_by_clues or search_titles.
4. title_details on the top one or two candidates, to confirm them against the clues.
5. Answer from the verdict lines at the top of the result. When it says "No candidate fits every clue", say that first, then give the closest candidate and the clue it does not fit; never claim a clue that is listed under "Does not match". One candidate that fits every clue: name it and say which clues it fits. Otherwise the best two or three, saying which clue each does not fit, and the single question that would tell them apart. None: say what was searched and ask for one more detail. A candidate that does not fit a clue the person was sure of is a near miss, and is called one.

## Reading what the database says

- Black and white is a tag the database may or may not have given a title. A title that is not tagged is not known to be in colour, so an untagged candidate that fits every other clue stays a candidate; say that the tag is missing rather than that the film is in colour.
- A credit that is announced or upcoming and has no IMDb id is named in words alone; it has no page, so title_details cannot read it. web_search is how to learn more about it.
- A clue listed under "Not checked" was not tested against that candidate. It is neither a fit nor a miss.

References: clue-words.md (how people describe looks and genres, and the database keywords they map to), search-phrasing.md (turning a remembered scene into a web query).
