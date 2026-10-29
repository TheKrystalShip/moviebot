using System.Globalization;
using TheKrystalShip.MovieBot.Api.Catalogue;
using TheKrystalShip.MovieBot.Catalogue;

namespace TheKrystalShip.MovieBot.Api.Votes;

/// <summary>A vote as the person sees it: the subject under the id the catalogue's routes take, named from the catalogue.</summary>
public sealed record VoteView(string Kind, string Id, int Vote, DateTimeOffset VotedAt, string? Name = null, string? Image = null,
    TitleCard? Card = null);

/// <summary>The body of a vote: 1 to like, -1 to dislike.</summary>
public sealed record SetVoteRequest
{
    public int Vote { get; init; }
}

/// <summary>
/// Describes stored votes from the catalogue, one batch read per kind however many votes there are. A
/// subject the catalogue no longer lists keeps only its kind, id, vote and time; a person it no longer
/// maps from the film database's id is left out, and is listed again if the catalogue maps it again.
/// </summary>
public sealed class VotePresenter(CatalogueQueries queries, CatalogueTitles titles, CatalogueViewers viewers)
{
    public List<VoteView> Views(string userId, IReadOnlyList<StoredVote> votes)
    {
        var join = viewers.For(userId);
        var cards = titles.Cards(votes.Where(v => v.Kind == VoteKinds.Title).Select(v => v.Id));
        var people = queries.PeopleOfTmdb([.. votes.Where(v => v.Kind == VoteKinds.Person).Select(v => int.Parse(v.Id, CultureInfo.InvariantCulture))]);
        var franchises = queries.FranchiseCards([.. votes.Where(v => v.Kind == VoteKinds.Franchise).Select(v => v.Id)]);
        var companies = queries.Companies([.. votes.Where(v => v.Kind == VoteKinds.Company).Select(v => long.Parse(v.Id, CultureInfo.InvariantCulture))]);

        var views = new List<VoteView>(votes.Count);
        foreach (var v in votes)
            switch (v.Kind)
            {
                case VoteKinds.Title:
                    views.Add(cards.TryGetValue(v.Id, out var card)
                        ? new VoteView(v.Kind, v.Id, v.Vote, v.VotedAt, card.Title, Card: join.Join(card))
                        : new VoteView(v.Kind, v.Id, v.Vote, v.VotedAt));
                    break;
                case VoteKinds.Person:
                    if (people.TryGetValue(int.Parse(v.Id, CultureInfo.InvariantCulture), out var person))
                        views.Add(new VoteView(v.Kind, person.Id.ToString(CultureInfo.InvariantCulture), v.Vote, v.VotedAt, person.Name, person.Portrait));
                    break;
                case VoteKinds.Franchise:
                    views.Add(franchises.TryGetValue(v.Id, out var franchise)
                        ? new VoteView(v.Kind, v.Id, v.Vote, v.VotedAt, franchise.Name, franchise.Poster)
                        : new VoteView(v.Kind, v.Id, v.Vote, v.VotedAt));
                    break;
                default:
                    views.Add(companies.TryGetValue(long.Parse(v.Id, CultureInfo.InvariantCulture), out var company)
                        ? new VoteView(v.Kind, v.Id, v.Vote, v.VotedAt, company.Name, company.Logo)
                        : new VoteView(v.Kind, v.Id, v.Vote, v.VotedAt));
                    break;
            }
        return views;
    }
}
