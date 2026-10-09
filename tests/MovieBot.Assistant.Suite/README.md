# MovieBot assistant suite

The behavioural cases of the assistant plugin, run against the live harness through the deployed
plugin, as a person who may watch in Krystal Cinema. `cases.json` holds the cases as data (a prompt,
follow-up turns, the tools expected, and what the reply must and must not say); the runner is the
plugin package's (`TheKrystalShip.Agent.Plugin.Testing`) and reports pass rate, tool-choice accuracy,
median and p90 turn time and prompt tokens.

```bash
export MOVIEBOT_ASSISTANT_TOKEN_FILE=/path/to/token      # a Cinema access token, read from the file
export MOVIEBOT_ASSISTANT_URL=https://movies.thekrystalship.com   # the default
dotnet run --project tests/MovieBot.Assistant.Suite -c Release -- --results /tmp/suite
```

- **The token is the person.** The file holds one access token for the audience `cinema` that lists
  `cinema:watch`; the suite sends it as the bearer on every turn and never prints it. The cases read
  the film database and the person's own history, so use an account whose history may be read.
- **The cases run against what is deployed.** A change to a tool, a description or a guard is a build
  and a deploy before the suite sees it; the prompt and skills can be tuned live (`Assistant:TuneFrom`).
- `--filter <text>` runs the cases whose id or group contains the text, `--list` lists them.
- **Sampling varies.** A case that fails once and passes the next run is a finding about the model,
  not about the plugin; read the transcript under the results directory before changing anything.
