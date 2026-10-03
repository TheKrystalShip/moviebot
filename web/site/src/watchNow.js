// Watch now, as every page starts it: the API answers with the room straight away and the page goes
// there, where the room shows the film being found, arriving and starting. A film already here goes
// to the room with the film in it.

import { toast } from "@thekrystalship/krystal-ui";
import { api } from "./api.js";
import { navigate } from "./router.js";

/** Starts watching and goes to the room; false when it could not start, which is said. */
export async function watchNow(request, { limit } = {}) {
  try {
    const reply = await api.watchNow(request);
    if (reply.outcome === "full") {
      toast.info(`The server is streaming ${limit ? `${limit} downloads` : "as many downloads as it can"} right now. Download it instead and you'll get a push when it's ready.`);
      return false;
    }
    if (!reply.room) {
      toast.error("That could not be started.");
      return false;
    }
    navigate(`/r/${encodeURIComponent(reply.room)}` + (reply.libraryId ? `?title=${encodeURIComponent(reply.libraryId)}` : ""));
    return true;
  } catch (error) {
    toast.fromError(error, "That could not be started");
    return false;
  }
}

/** The release Watch now and Download pick by default for a film: 1080p where it is offered. */
export function defaultOffer(availability) {
  return (availability && (availability.hd1080 || availability.hd720)) || null;
}
