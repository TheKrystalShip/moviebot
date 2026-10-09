using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheKrystalShip.Agent.Contracts;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant;
using TheKrystalShip.MovieBot.Assistant.Tools;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests.Assistant;

/// <summary>The profile the service ships: its prompt, tools, skills and guard data, read the way the harness is given them.</summary>
public sealed partial class ProfileTests
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "profile");

    private static readonly string[] Tools =
        ["search_titles", "title_details", "person_credits", "discover", "find_by_clues", "whats_new", "my_taste"];

    private static readonly string[] Skills =
        ["filmography", "find-a-forgotten-film", "recommend", "taste-survey", "whats-on"];

    /// <summary>The names the harness keeps for itself, which a plugin's tool may not take.</summary>
    private static readonly string[] Reserved =
        ["use_skill", "remember", "recall", "forget", "fetch_url", "web_search", "ask_question", "tool_search", "tool_describe", "question"];

    private static PluginProfile Load() => PluginProfileLoader.Load(Directory, PluginIdentity.Id, "test");

    [Fact]
    public void The_profile_has_the_seven_tools_in_the_order_the_model_is_offered_them()
    {
        Assert.Equal(Tools, Load().Tools.Select(t => t.Name));
    }

    [Fact]
    public void Every_tool_reads_and_none_is_held_back_for_tool_search()
    {
        Assert.All(Load().Tools, t =>
        {
            Assert.Equal(ToolClasses.Read, t.Class);
            Assert.False(t.Deferred, t.Name);
        });
    }

    [Fact]
    public void Every_tool_has_a_name_in_the_catalogs_form_a_description_and_an_object_schema()
    {
        foreach (ProfileTool tool in Load().Tools)
        {
            Assert.Matches("^[a-z][a-z0-9_]*$", tool.Name);
            Assert.DoesNotContain(tool.Name, Reserved);
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), tool.Name);
            Assert.Equal(JsonValueKind.Object, tool.Parameters.ValueKind);
            Assert.Equal("object", tool.Parameters.GetProperty("type").GetString());
        }
    }

    [Fact]
    public void Every_required_parameter_is_declared_and_every_parameter_is_typed_and_described()
    {
        foreach (ProfileTool tool in Load().Tools)
        {
            JsonElement properties = tool.Parameters.GetProperty("properties");
            HashSet<string> declared = [.. properties.EnumerateObject().Select(p => p.Name)];
            if (tool.Parameters.TryGetProperty("required", out JsonElement required))
                Assert.All(required.EnumerateArray().Select(r => r.GetString()!), name => Assert.Contains(name, declared));

            foreach (JsonProperty parameter in properties.EnumerateObject())
            {
                Assert.True(parameter.Value.TryGetProperty("type", out _), $"{tool.Name}.{parameter.Name} has no type");
                Assert.True(parameter.Value.TryGetProperty("description", out JsonElement d) && d.GetString()!.Length > 0,
                    $"{tool.Name}.{parameter.Name} has no description");
            }
        }
    }

    [Fact]
    public void The_required_parameters_are_the_ones_the_tools_cannot_run_without()
    {
        static string[] Required(ProfileTool t) =>
            t.Parameters.TryGetProperty("required", out JsonElement r) ? [.. r.EnumerateArray().Select(v => v.GetString()!)] : [];

        PluginProfile profile = Load();
        Assert.Equal(["query"], Required(profile.Tools.Single(t => t.Name == "search_titles")));
        Assert.Equal(["title"], Required(profile.Tools.Single(t => t.Name == "title_details")));
        Assert.Equal(["person"], Required(profile.Tools.Single(t => t.Name == "person_credits")));
        Assert.Equal(["which"], Required(profile.Tools.Single(t => t.Name == "whats_new")));
        Assert.Empty(Required(profile.Tools.Single(t => t.Name == "discover")));
        Assert.Empty(Required(profile.Tools.Single(t => t.Name == "my_taste")));
    }

    [Fact]
    public void The_values_a_tool_accepts_are_the_ones_its_code_resolves()
    {
        PluginProfile profile = Load();

        static string[] Enum(ProfileTool t, string p) =>
            [.. t.Parameters.GetProperty("properties").GetProperty(p).GetProperty("enum").EnumerateArray().Select(v => v.GetString()!)];

        Assert.Equal(["film", "show"], Enum(profile.Tools.Single(t => t.Name == "search_titles"), "kind"));
        Assert.Equal(["acting", "directing", "writing", "any"], Enum(profile.Tools.Single(t => t.Name == "person_credits"), "role"));
        Assert.Equal(["popular", "rating", "newest", "oldest"], Enum(profile.Tools.Single(t => t.Name == "discover"), "sort"));
        Assert.Equal(["trending", "released", "upcoming", "library"], Enum(profile.Tools.Single(t => t.Name == "whats_new"), "which"));
    }

    [Fact]
    public void The_descriptions_are_the_text_the_design_signed_off_with_the_two_changes_the_database_forces()
    {
        PluginProfile profile = Load();
        string First(string tool) => profile.Tools.Single(t => t.Name == tool).Description.Split('\n')[0];

        Assert.Equal("Find films and shows by name, or by words from a name.", First("search_titles"));
        Assert.Equal("Everything the film database knows about one film or show.", First("title_details"));
        Assert.Equal("An actor's or film-maker's work, in date order, oldest first.", First("person_credits"));
        Assert.Equal("Find films or shows by what they are like rather than by name. Every filter is optional and they combine.", First("discover"));
        Assert.Equal("What is current: trending this week, out recently, or coming soon.", First("whats_new"));
        Assert.StartsWith("What the asking person has watched, rated and saved", First("my_taste"));

        string details = profile.Tools.Single(t => t.Name == "title_details").Description;
        Assert.Contains("whether it is tagged black and white", details);
        Assert.DoesNotContain("colour", details);
    }

    [Fact]
    public void The_skills_are_the_five_the_design_names_each_with_a_body()
    {
        PluginProfile profile = Load();

        Assert.Equal(Skills, profile.Skills.Select(s => s.Name));
        Assert.All(profile.Skills, s =>
        {
            Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", s.Name);
            Assert.InRange(s.Name.Length, 1, ProfileSkill.MaxNameLength);
            Assert.InRange(s.Description.Length, 1, ProfileSkill.MaxDescriptionLength);
            Assert.False(string.IsNullOrWhiteSpace(s.Body), s.Name);
        });
    }

    [Fact]
    public void The_forgotten_film_skill_carries_its_two_reference_files()
    {
        ProfileSkill skill = Load().Skills.Single(s => s.Name == "find-a-forgotten-film");

        Assert.Equal(["references/clue-words.md", "references/search-phrasing.md"], skill.References.Keys.Order());
        Assert.All(skill.References.Values, text => Assert.False(string.IsNullOrWhiteSpace(text)));
        Assert.Contains("clue-words.md", skill.Body);
        Assert.Contains("search-phrasing.md", skill.Body);
    }

    [Fact]
    public void The_recommend_skill_reads_the_person_before_it_suggests_and_the_survey_writes_the_note_it_reads()
    {
        PluginProfile profile = Load();
        string recommend = profile.Skills.Single(s => s.Name == "recommend").Body;
        string survey = profile.Skills.Single(s => s.Name == "taste-survey").Body;

        Assert.Contains("recall taste", recommend);
        Assert.Contains("my_taste", recommend);
        Assert.Contains("Leave out everything they already watched", recommend);
        Assert.Contains("Six to eight", recommend);
        Assert.Contains("remember", recommend);

        Assert.Contains("ask_question", survey);
        Assert.Contains("at most four questions", survey);
        Assert.Contains("remember under taste", survey);
        Assert.Contains("recommend", survey);
    }

    [Fact]
    public void Every_tool_a_skill_or_the_prompt_names_is_one_the_assistant_has()
    {
        PluginProfile profile = Load();
        HashSet<string> known = [.. Tools, .. Reserved];
        string text = profile.PromptAppendix + "\n" + string.Join('\n', profile.Skills.Select(s => s.Body));

        foreach (Match m in SnakeCase().Matches(text))
            Assert.True(known.Contains(m.Value) || m.Value is "good_only", $"'{m.Value}' is not a tool");
    }

    [GeneratedRegex(@"\b[a-z]+(?:_[a-z]+)+\b")]
    private static partial Regex SnakeCase();

    [Fact]
    public void The_prompt_is_the_appendix_the_design_signed_off()
    {
        string prompt = Load().PromptAppendix;

        Assert.StartsWith("On Krystal Cinema a group of friends watch films and shows together.", prompt);
        Assert.Contains("Never state one from your own memory", prompt);
        Assert.Contains("load find-a-forgotten-film", prompt);
        Assert.Contains("(recall taste)", prompt);
        Assert.Contains("Every title you name gets a card", prompt);
        Assert.InRange(prompt.Length, 1200, 2200);
    }

    [Fact]
    public void The_prefix_the_model_reads_holds_nothing_that_changes_between_turns()
    {
        PluginProfile profile = Load();
        string text = profile.PromptAppendix + string.Join('\n', profile.Tools.Select(t => t.Description));

        Assert.DoesNotMatch(@"\b20\d\d-\d\d-\d\d\b", text);
        Assert.DoesNotContain("today is", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nothing_in_the_profile_names_the_assistant()
    {
        PluginProfile profile = Load();
        var all = new StringBuilder(profile.PromptAppendix);
        foreach (ProfileTool t in profile.Tools)
            all.Append('\n').Append(t.Name).Append('\n').Append(t.Description).Append('\n').Append(t.Parameters.GetRawText());
        foreach (ProfileSkill s in profile.Skills)
        {
            all.Append('\n').Append(s.Name).Append('\n').Append(s.Description).Append('\n').Append(s.Body);
            foreach (string reference in s.References.Values)
                all.Append('\n').Append(reference);
        }

        all.Append('\n').Append(profile.Guards.ActionClaims?.Correction).Append(profile.Guards.ActionClaims?.RetryNotice);
        all.Append('\n').Append(string.Join('\n', profile.Speech?.Vocabulary ?? []));

        Assert.DoesNotContain("moviebot", all.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("movie bot", all.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_sentence_the_service_writes_names_the_assistant()
    {
        string source = SourceDirectory();
        foreach (string file in System.IO.Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                 && Path.GetFileName(f) != "PluginIdentity.cs"))
        {
            foreach (Match literal in StringLiteral().Matches(File.ReadAllText(file)))
                Assert.False(
                    literal.Value.Contains("moviebot", StringComparison.OrdinalIgnoreCase) && !literal.Value.Contains("moviebot.settings"),
                    $"{Path.GetFileName(file)}: {literal.Value}");
        }
    }

    [GeneratedRegex("\"(?:[^\"\\\\\\n]|\\\\.)*\"")]
    private static partial Regex StringLiteral();

    private static string SourceDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "src", "MovieBot.Assistant");
            if (System.IO.Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("src/MovieBot.Assistant was not found above the test binaries.");
    }

    [Fact]
    public void The_guards_hold_figures_to_the_tools_and_catch_a_claim_of_acting()
    {
        ProfileGuards guards = Load().Guards;

        Assert.True(guards.FabricatedFigures);
        Assert.NotNull(guards.ActionClaims);
        Assert.Contains("added", guards.ActionClaims!.CompletedVerbs);
        Assert.Contains("add", guards.ActionClaims.OfferedVerbs);
        Assert.False(string.IsNullOrWhiteSpace(guards.ActionClaims.Correction));
    }

    [Fact]
    public void The_titles_claim_captures_each_title_a_reply_names_and_nothing_before_it()
    {
        ClaimPattern claim = Assert.Single(Load().Guards.Claims!);
        var regex = new Regex(
            claim.Pattern, claim.CaseInsensitive ? RegexOptions.IgnoreCase : RegexOptions.None, TimeSpan.FromMilliseconds(250));
        string[] Claimed(string reply) => [.. regex.Matches(reply).Select(m => m.Groups[claim.Group].Value.Trim())];

        Assert.Equal("titles", claim.Name);

        // A list: one title a line, after a marker or a number.
        Assert.Equal(["The Others", "Heat"], Claimed("Here are some picks:\n- The Others (2001) - a ghost story\nHeat (1995), 15 December, released"));

        // A leading number is taken for a list number, so a title that starts with one loses it; what is left is
        // still a part of the title and is found in the result the title came from.
        Assert.Equal(["Days Later"], Claimed("2. **28 Days Later** (2002)"));

        // A sentence claims the title and not the words before it.
        Assert.Equal(["The Lighthouse"], Claimed("You might be thinking of The Lighthouse (2019). It is a black and white film."));
        // Bold hides the first word from the lookbehind; the rest of the title is still found in the result.
        Assert.Equal(["Lighthouse"], Claimed("It sounds like you're thinking of **The Lighthouse (2019)**."));
        Assert.Equal(["A Tale of Two Sisters"], Claimed("Is the film you're thinking of A Tale of Two Sisters (2003)?"));
        Assert.Equal(["Witches of Eastwick"], Claimed("Is it possible you're thinking of *The Witches of Eastwick* (1987), which stars Jack Nicholson?"));
        Assert.Equal(["Mad Max: Fury Road"], Claimed("Yes, Mad Max: Fury Road (2015) is a good one."));
        Assert.Equal(["Lee Cronin's The Mummy"], Claimed("Lee Cronin's The Mummy (2026) - a mystery"));

        // A title is one line of words, and a decade or a sentence before it is not part of it.
        Assert.Equal(["The Others", "Saw"], Claimed("Here are films from the early 2000s:\nThe Others (2001)\nA survival horror film in a post-outbreak Britain. Saw (2004)"));
        Assert.Equal(["The Others"], Claimed("Films from the early 2000s: The Others (2001) is one."));

        // Not a title: a year in brackets after a lowercase phrase, or a sentence with none.
        Assert.Empty(Claimed("She was born in 1950 (aged 12)\nAnne Hathaway appeared in the following movies this year:"));
        Assert.Empty(Claimed("it came out later (2019) than they said"));
    }

    [Fact]
    public void The_forgotten_film_answer_ends_by_inviting_more_clues_and_names_the_clue_that_did_not_fit()
    {
        PluginProfile profile = Load();
        string skill = profile.Skills.Single(s => s.Name == "find-a-forgotten-film").Body;

        Assert.Contains("say plainly which clue it does not fit", skill);
        Assert.Contains("other actors in it, a scene you remember, or whether it is recent", skill);
        Assert.Contains("not a question to ask with ask_question", skill);
        Assert.Contains("invite more clues", profile.PromptAppendix);
    }

    [Fact]
    public void The_profile_is_valid_and_hashes_the_same_each_time_it_is_read()
    {
        PluginProfile first = Load();

        Assert.Equal(first.ContentHash(), Load().ContentHash());
        Assert.Equal(first.ContentHash(), PluginProfile.FromJson(first.ToJson())!.ContentHash());
    }

    [Fact]
    public void The_tool_classes_the_service_registers_are_the_catalogs_tools()
    {
        string[] registered =
        [
            .. typeof(CinemaTool).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(CinemaTool)))
                .Select(t => t.Name),
        ];

        Assert.Equal(
            ["DiscoverTool", "FindByCluesTool", "MyTasteTool", "PersonCreditsTool", "SearchTitlesTool", "TitleDetailsTool", "WhatsNewTool"],
            registered.Order());
    }
}
