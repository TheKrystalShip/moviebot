// A page's orders as its filter drawer offers them. Each is a sort key and a direction under the words a
// person reads ("Highest rated", "Lowest rated"), and every order a page offers comes with its opposite.
// The drawer holds an order as `key:dir`; the page keeps it as `{key, dir}`, as `useSort` does.

/** The id of the drawer's order section. */
export const ORDER = "sort";

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
