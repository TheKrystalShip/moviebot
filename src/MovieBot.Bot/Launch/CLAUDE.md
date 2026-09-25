# Launch cards: when a launch stops opening

A launch card is a door. The room behind it closes, and left alone the card reads exactly as it did
when it was posted: the same film, the same button, nothing anywhere to say why pressing it does
nothing.

- **An invite is made to last the film.** Discord counts an invite's life from the moment it is made,
  never from the last person through it, so the invite is given the film's running time plus
  `Launch:InviteGraceSeconds`. A flat window shorter than a film shuts the door on a room that is
  still watching: everybody inside carries on and nobody else can get in, which is invisible from both
  sides.
- **The grace matches the API's `Rooms:IdleTimeout`**, which lands the invite and the room behind it
  at the same moment rather than leaving one to outlive the other.
- **An invite always expires.** An age of zero reads as never to Discord, so a floor stands under
  whatever is configured. A permanent way into the server is not a thing a film hands out.
- **A card that has stopped being a way in says so where it stands.** It goes grey, the button and the
  link on its title go, and the description says what happened and that `/watch` in a voice channel
  starts the film again. The film, the poster and the fields stay: scrolling back to what an evening
  watched is worth being able to do.
- **A room that moved on to another film is told apart from a room that closed.** One is still
  watching and this card describes the wrong thing; the other is over. A room holding nothing counts
  as closed, since a room forgotten and opened again by somebody arriving is an empty room wearing the
  same name.
- **The invite is revoked with the card**, so a link copied out of one stops working when the card
  says it has.
- **A card is written down, because it exists nowhere else.** Which message, in which channel,
  carrying which invite, for which film. The API has never heard of a Discord message, so an unwritten
  card is one a restarted bot leaves looking live for good.
- **A pass that cannot reach the API does nothing.** Rooms it could not read are not rooms that
  closed, and marking every card over on a moment's trouble is worse than the silence it replaces.
- **The card is edited, never replaced.** An edit notifies nobody, which is right: this is for whoever
  scrolls back and finds it, not for the room that has already moved on.
