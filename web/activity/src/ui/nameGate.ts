/**
 * Asks a first-time viewer what to call them.
 *
 * The launch link carries no identity, so the name comes from the person and is kept in this
 * browser. It is asked once: every later visit reads what was stored.
 */
const MaxLength = 32;

export function askForDisplayName(): Promise<string> {
  return new Promise((resolve) => {
    const gate = document.createElement('div');
    gate.className = 'name-gate';
    gate.innerHTML = `
      <form class="name-gate__card">
        <label class="name-gate__label" for="name-gate-input">What should the room call you?</label>
        <input class="name-gate__input" id="name-gate-input" type="text" autocomplete="nickname"
               maxlength="${MaxLength}" required />
        <button class="name-gate__button" type="submit">Watch</button>
      </form>`;

    const input = gate.querySelector('.name-gate__input') as HTMLInputElement;
    const form = gate.querySelector('.name-gate__card') as HTMLFormElement;

    form.addEventListener('submit', (event) => {
      event.preventDefault();
      const name = input.value.trim().slice(0, MaxLength);
      if (name === '') {
        input.focus();
        return;
      }
      gate.remove();
      resolve(name);
    });

    document.body.appendChild(gate);
    input.focus();
  });
}
