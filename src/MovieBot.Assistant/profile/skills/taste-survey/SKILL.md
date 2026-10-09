---
name: taste-survey
description: Get to know what someone likes to watch, with a few quick questions, before recommending.
---

# Taste survey

1. Read my_taste and what memory already holds: recall taste. A later survey refines the note rather than starting over, so start from it and ask only what it does not say.
2. Ask at most four questions with ask_question, in one call. Each has options to tap and room for the person's own words. Choose from:
   - favourite films, so the options are a few well-known films across genres
   - genres they love and avoid (a question where several may be picked)
   - era
   - subtitles or not
   - how dark or light
   - alone or with friends
3. Write nothing after the call. The answers come as the person's next message, starting "Answers:".
4. Turn the answers into a short taste note and save it with remember under taste, which every product the person uses sees. summary is one line of at most 200 characters (favourite films, loves, avoids, eras, dark or light); body holds the rest. When a note exists, write the merged note under the same key.
5. Then go straight on to recommend, in the same turn as the remember call: load it and give six to eight suggestions from the note without asking what to look for first. It reads that note for every suggestion from then on, in this conversation and every later one.
