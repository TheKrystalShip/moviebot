import React from "react";
import { Button, Icon } from "@thekrystalship/krystal";
import { ago } from "../time.js";
import { Person } from "./Person.jsx";
import "./invite-people.css";

/**
 * Everybody who can be asked in, each with Invite. An invite is a push to their devices, or a waiting
 * notification where no push reaches them, and the row says which went out and nothing about whether
 * they saw it. The people already in (`here`, their ids) carry `hereMark` instead of the button and,
 * when given, `hereStatus` as their line. `load` lists the people (`GET /api/people`);
 * `invite(person)` resolves to the delivery the API reports, `push` or `bell`.
 */
export function InvitePeople({ load, invite, here, hereMark, hereStatus, label = "Invite people", className = "" }) {
  const [people, setPeople] = React.useState(null);
  const [sent, setSent] = React.useState({});
  React.useEffect(() => {
    let live = true;
    load().then((list) => live && setPeople(list), () => live && setPeople([]));
    return () => { live = false; };
  }, [load]);

  const ask = async (person) => {
    setSent((s) => ({ ...s, [person.userId]: "sending" }));
    try {
      const delivery = await invite(person);
      setSent((s) => ({ ...s, [person.userId]: delivery }));
    } catch {
      setSent((s) => ({ ...s, [person.userId]: "failed" }));
    }
  };
  const status = (person) => {
    if (here.has(person.userId) && hereStatus) return hereStatus;
    const state = sent[person.userId];
    if (state === "push") return "Push sent · waiting for them";
    if (state === "bell") return "In their notifications · waiting for them";
    if (state === "failed") return "Could not be invited";
    return person.lastSeenAt ? `Active ${ago(person.lastSeenAt)}` : "Not seen on the site";
  };

  if (!people || people.length === 0) return null;
  return (
    <section className={"kc-invite-people" + (className ? " " + className : "")}>
      {label && <span className="kc-invite-people__label">{label}</span>}
      <div className="kc-invite-people__list">
        {people.map((person) => {
          const state = sent[person.userId];
          return (
            <div key={person.userId} className="kc-invite-people__person">
              <Person person={{ userId: person.userId, displayName: person.name }} className="kc-invite-people__avatar" />
              <div className="kc-invite-people__who">
                <span className="kc-invite-people__name">{person.name}</span>
                <span className={"kc-invite-people__status" + (state === "failed" ? " is-failed" : "")}>{status(person)}</span>
              </div>
              {here.has(person.userId)
                ? <span className="kc-invite-people__here">{hereMark}</span>
                : state === "push" || state === "bell"
                  ? <span className="kc-invite-people__sent"><Icon name="check" size={13} />Invited</span>
                  : <Button variant="outline" size="sm" busy={state === "sending"} onClick={() => ask(person)}>Invite</Button>}
            </div>
          );
        })}
      </div>
    </section>
  );
}
