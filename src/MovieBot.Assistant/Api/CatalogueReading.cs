using TheKrystalShip.MovieBot.Catalogue;

namespace TheKrystalShip.MovieBot.Assistant.Api;

/// <summary>What the tools read off the catalogue's shapes more than once.</summary>
internal static class CatalogueReading
{
    extension(TitleCard card)
    {
        /// <summary>Whether the library holds the title, as the API joined it for the asking person.</summary>
        public bool InLibrary => card.You?.InLibrary == true;

        /// <summary>Whether the asking person watched the title through.</summary>
        public bool Watched => card.You?.Watched == true;
    }

    extension(Facets facets)
    {
        /// <summary>One facet section's options; empty when the catalogue has no such section.</summary>
        public List<FacetOption> Section(string id) => facets.Sections.FirstOrDefault(s => s.Id == id)?.Options ?? [];
    }
}
