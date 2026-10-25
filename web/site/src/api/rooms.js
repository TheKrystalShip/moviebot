// Rooms and the people in them: the rooms open now, one room as it stands, who can be asked in, and
// asking them.

import { call, enc, withQuery } from "./http.js";

export const rooms = {
  list: () => call("GET", "/api/sessions"),
  // One room as it stands, which lets the person in when it admits them; a 403 is a room private to them.
  get: (id) => call("GET", `/api/sessions/${enc(id)}`),
  people: (q = null) => call("GET", withQuery("/api/people", { q })),
  invite: (room, userId) => call("POST", `/api/rooms/${enc(room)}/invite`, { userId }),
};
