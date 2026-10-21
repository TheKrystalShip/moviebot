/** A day as a wall calendar shows it: the weekday over the date, the month under it. `at` is a Date or an instant. */
export function DateTile({ at }) {
  const day = new Date(at);
  return (
    <span className="kc-date-tile" aria-hidden="true">
      <span className="kc-date-tile__weekday">{day.toLocaleDateString("en-GB", { weekday: "short" })}</span>
      <span className="kc-date-tile__day">{day.getDate()}</span>
      <span className="kc-date-tile__month">{day.toLocaleDateString("en-GB", { month: "short" })}</span>
    </span>
  );
}
