// The API client, one module per kind of thing it reaches: the catalogue (what titles are), the library
// (what this server holds and fetches), rooms, and the signed-in person's own records.

export { ApiError } from "./http.js";
export { catalogue, pagesOf, queryParams, narrowed, cardName, cardRating, ratedBy, posterArt, isFilm, certificateOf, runtimeText } from "./catalogue.js";
export { library as libraryApi, posterUrl, filmName, episodeCode } from "./library.js";
export { rooms as roomsApi } from "./rooms.js";
export { me as meApi } from "./me.js";
