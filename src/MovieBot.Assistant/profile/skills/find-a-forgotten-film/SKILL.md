---
name: find-a-forgotten-film
description: Identify a film or show someone half remembers from a scene, a trailer, an actor, a plot detail or roughly when they saw it.
---

# Find a forgotten film

1. Write the clues down as facts and how sure the person is of each: people, era, look (black and white, animated, foreign), genre, plot or scene details, where they saw it (a trailer, a festival, TV as a child). Turn every clue about when into years from today's date in the reminder: "recently announced" is this year or the year before, and a film made long before that does not fit it.
2. Start from the firmest clue.
   - A person: person_credits over the right years, announced titles included. "Recently", "announced" or "coming out" means from last year's number on, using today's date in the reminder: set from. Never read it as a film from years ago.
   - An era and a genre: discover with about keywords.
   - A person and a look together: also discover with with set to the person and about set to the look (for black and white, about "black and white" and good_only false); then the years from person_credits tell which of them is recent. A title the database tags with the look and the person, whose dates do not fit, is still worth naming as the near miss.
   - A scene or a trailer: web_search describing it in the words a review or a news story would use.
3. Check every candidate with title_details against each clue. Drop any that contradict a firm clue.
4. One match: name it, say which clues it fits. Several: the best two or three, and the single question that would tell them apart. None: say what was searched and ask for one more detail.

## Reading what the database says

- Black and white is a tag the database may or may not have given a title. A title that is not tagged is not known to be in colour, so an untagged candidate that fits every other clue stays a candidate; say that the tag is missing rather than that the film is in colour.
- When the person says it was announced or came out recently, that is a clue about the year and the status: look at the person's upcoming and announced credits and the newest released ones, from this year on, and drop films from years ago. A film that fits the look and the person but was made long before the clue's time is a different film; say so and keep looking.
- A credit that is announced or upcoming and has no IMDb id is named in words alone; it has no page, so title_details cannot read it. web_search is how to learn more about it.
- discover's Matched line says which genres, people and keywords each of your words became. When a word matched something other than you meant, say so and try another word.

References: clue-words.md (how people describe looks and genres, and the database keywords they map to), search-phrasing.md (turning a remembered scene into a web query).
