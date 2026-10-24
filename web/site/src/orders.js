// A page's orders as its filter drawer offers them. Each is a sort key and a direction under the words a
// person reads ("Highest rated", "Lowest rated"), and every order a page offers comes with its opposite.
// The drawer holds an order as `key:dir`; the page keeps it as `{key, dir}`, as `useSort` does.

/** The id of the drawer's order section. */
export const ORDER = "sort";

/**
 * The words for each order the API serves a list in, by the name its routes take (`sort`), and the key
 * and direction a page keeps the order as (`useSort`). An order the API names that has none here is
 * offered by its name.
 */
export const ORDER_WORDS = {
  order: { label: "Shelf order", key: "order", dir: "asc" },
  popular: { label: "Most popular", key: "popular", dir: "desc" },
  "least-popular": { label: "Least popular", key: "popular", dir: "asc" },
  added: { label: "Recently added", key: "added", dir: "desc" },
  "first-added": { label: "First added", key: "added", dir: "asc" },
  name: { label: "Name A–Z", key: "name", dir: "asc" },
  "name-desc": { label: "Name Z–A", key: "name", dir: "desc" },
  rating: { label: "Highest rated", key: "rating", dir: "desc" },
  lowest: { label: "Lowest rated", key: "rating", dir: "asc" },
  newest: { label: "Newest first", key: "year", dir: "desc" },
  oldest: { label: "Oldest first", key: "year", dir: "asc" },
  shortest: { label: "Shortest first", key: "length", dir: "asc" },
  longest: { label: "Longest first", key: "length", dir: "desc" },
  "most-wanted": { label: "Most wanted", key: "others", dir: "desc" },
  "least-wanted": { label: "Least wanted", key: "others", dir: "asc" },
};

/** An order as the API names it, with its words, key and direction. */
export const orderOf = (sort) => ({ sort, ...(ORDER_WORDS[sort] || { label: sort, key: sort, dir: "desc" }) });

/** The API's name for an order kept as a key and a direction, among `sorts` when given. */
export const sortOf = ({ key, dir }, sorts = Object.keys(ORDER_WORDS)) =>
  sorts.find((s) => ORDER_WORDS[s] && ORDER_WORDS[s].key === key && ORDER_WORDS[s].dir === dir);

const valueOf = ({ key, dir }) => key + ":" + dir;

/** The drawer's sort section over a page's `orders`, each `{ key, dir, label }`, the first the page's own. */
export const orderSection = (orders) => ({
  id: ORDER, label: "Sort by", kind: "sort", base: valueOf(orders[0]),
  options: orders.map((o) => ({ value: valueOf(o), label: o.label })),
});

/**
 * A drawer's sections, values and change with a page's order in them: the order's section first, the
 * order in force as its value, and a pick of another order sent to `pick(key, dir)` while every other
 * change goes on to `onValues`.
 */
export function withOrder({ sections, values, onValues }, orders, order, pick) {
  const current = valueOf(order);
  return {
    sections: [orderSection(orders), ...sections],
    values: { ...values, [ORDER]: current },
    onValues: (next) => {
      const { [ORDER]: picked, ...rest } = next;
      if (picked && picked !== current) {
        const [key, dir] = picked.split(":");
        pick(key, dir);
        return;
      }
      onValues(rest);
    },
  };
}

/** How many of a page's titles are shown: "248 movies", or "25 of 248 movies" while some are not. */
export const shownOf = (shown, all, [one, many]) =>
  (shown < all ? `${shown} of ${all} ${all === 1 ? one : many}` : `${all} ${all === 1 ? one : many}`);
