---
name: find-a-forgotten-film
description: Identify a film or show someone half remembers from a scene, a trailer, an actor, a plot detail or roughly when they saw it.
---

# Find a forgotten film

1. Extract the clues from what the person said, one by one: people, years, whether it is out yet, look (black and white, animated), plot words, language. Turn every clue about when into years from today's date in the reminder: "recently announced" is this year or the year before, with released set to upcoming or announced; "last summer" is last year. Do not add a clue they did not give.
2. When the person names a character instead of an actor ("the actress who played Amy Pond"), the first call is web_search for who played the character, such as "who played Amy Pond in Doctor Who". Never name the actor from memory and never pass a guessed name to person_credits or find_by_clues: the search result names them. Then carry on with that name as the people clue.
3. Call find_by_clues once with every clue. It searches the database and ranks the candidates by how many clues each fits, and says which it fits and which it does not.
4. web_search only for a clue find_by_clues could not use: a scene, a trailer, a rumour, news. Search in the words a review or a news story would use, then look the title up with find_by_clues or search_titles.
5. title_details on the top one or two candidates, to confirm them against the clues.
6. Answer from the verdict lines at the top of the result. Clues people give are often a little wrong, so a candidate that matches the people and the look but misses the year is still the answer to offer. Name the best candidate with its year, say which clues it fits, and say plainly which clue it does not fit, such as "it came out in 2019, so not recent". Never claim a clue that is listed under "Does not match". If the tool found several close ones, give the best two or three and the single question that would tell them apart. None: say what was searched.
7. End every answer to a half-remembered film by inviting more clues, in one sentence that names the kinds that would help most given what is missing: "If this is not what you had in mind, tell me more: other actors in it, a scene you remember, or whether it is recent (the last couple of years)." It is a sentence in the reply, not a question to ask with ask_question.

## Reading what the database says

- Black and white is a tag the database may or may not have given a title. A title that is not tagged is not known to be in colour, so an untagged candidate that fits every other clue stays a candidate; say that the tag is missing rather than that the film is in colour.
- A credit that is announced or upcoming and has no IMDb id is named in words alone; it has no page, so title_details cannot read it. web_search is how to learn more about it.
- A clue listed under "Not checked" was not tested against that candidate. It is neither a fit nor a miss.

References: clue-words.md (how people describe looks and genres, and the database keywords they map to), search-phrasing.md (turning a remembered scene into a web query).
