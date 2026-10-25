// MovieBot.Api, as the site reaches it: same origin, the session's access token as the bearer.
//
// This is the one module that attaches the token, through the session's `authorizedFetch`, which
// renews and replays a refused call once. A 401 that survives that is a session the API will not
// honour: it is ended, which the page answers by saying so and offering the way back rather than
// showing a library that failed to load. A 403 is the person's account not letting them do what they
// asked.
//
// Every call tells `connectivity.js` whether the server answered: a call that fails on the network,
// or is answered by a gateway or a captive portal rather than by MovieBot, is the page going offline,
// and anything the server answers, an error among it, is the page online.

import { answeredByServer, reached, unreached } from "../connectivity.js";
import { queryOf } from "../query.js";
import { session } from "../session.js";

export class ApiError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

const UNANSWERED = "MovieBot is not answering. Try again in a moment.";
const SIGN_IN = "Sign in to carry on.";

/** The request with the bearer when somebody is signed in, and without it when nobody is. */
function authorized(path, init) {
  return session.authorizedFetch(path, init).catch((error) => {
    if (error && error.code === "none") return fetch(path, init);
    throw error;
  });
}

async function send(method, path, body) {
  const headers = { Accept: "application/json" };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  let response;
  try {
    response = await authorized(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  } catch (error) {
    if (error && error.code === "ended") throw new ApiError(401, SIGN_IN);
    unreached();
    throw new ApiError(0, UNANSWERED);
  }
  if (!answeredByServer(response)) {
    unreached();
    throw new ApiError(0, UNANSWERED);
  }
  reached();
  return response;
}

/** One call: its JSON answer, null for 204, an `ApiError` carrying the API's own reason for anything else. */
export async function call(method, path, body) {
  const response = await send(method, path, body);

  if (response.status === 401) {
    if (session.getSnapshot().status === "live") session.end("refused");
    throw new ApiError(401, SIGN_IN);
  }
  if (response.status === 204) return null;

  const payload = await response.json().catch(() => null);
  if (!response.ok) throw new ApiError(response.status, (payload && payload.error) || `MovieBot answered ${response.status}.`);
  return payload;
}

export const enc = encodeURIComponent;

/** A path with named values as its query (`queryOf`): a list repeats its key, an empty value is left out. */
export const withQuery = (path, params) => {
  const query = queryOf(params);
  return query ? `${path}?${query}` : path;
};
