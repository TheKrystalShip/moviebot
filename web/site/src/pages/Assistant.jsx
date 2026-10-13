import { CinemaAssistant } from "../CinemaAssistant.jsx";
import { useSession } from "../session.js";

/** The assistant as a page: the chat filling the column, as a phone's tab opens it. */
export function Assistant() {
  return (
    <div className="kc-chat-page">
      <CinemaAssistant session={useSession()} />
    </div>
  );
}
