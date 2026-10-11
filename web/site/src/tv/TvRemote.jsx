import { Button, Icon, toast } from "@thekrystalship/krystal-ui";
import { dayOf } from "../time.js";
import { Qr } from "./Qr.jsx";
import { unpairPhone, usePairedPhones, usePairingCode } from "./screen.js";

/**
 * Profile's Remote on a television: the code a phone scans to pair with it, and the phones paired with
 * it, each with when it paired and Unpair. Unpair is the row's one control, so the remote moves down the
 * list from one to the next, and the row it is in lifts with it.
 */
export function TvRemote() {
  const code = usePairingCode(true);
  const phones = usePairedPhones();
  return (
    <div className="kc-tvr">
      <figure className="kc-tvr__code">
        {code && <Qr value={code} quiet={2} className="kc-tvr__qr" label="Code to pair a phone" />}
      </figure>
      {phones && phones.length > 0 && (
        <section className="kc-tvr__phones" aria-label="Paired phones">
          <h2 className="kc-tvr__label">Paired phones</h2>
          {phones.map((phone) => (
            <div key={phone.id} className="kc-tvr__phone">
              <Icon name="smartphone" size={22} />
              <span className="kc-tvr__text">
                <span className="kc-tvr__name">{phone.name}</span>
                <span className="kc-tvr__when">Paired {dayOf(phone.pairedAt)}</span>
              </span>
              <Button size="sm" onClick={() => unpairPhone(phone.id).catch((error) => toast.fromError(error, "Not unpaired"))}>
                Unpair
              </Button>
            </div>
          ))}
        </section>
      )}
    </div>
  );
}

/** The television's name beside the section's title, as its session at the provider carries it. */
export function TvRemoteName({ name }) {
  return name ? <span className="kc-tvr__screen"><Icon name="tv" size={15} />{name}</span> : null;
}

/**
 * The code beside the paused player's card on a television, centred in the same box, under the word
 * Remote. Nothing is drawn until the television has a code to show.
 */
export function PausedCode() {
  const code = usePairingCode(true);
  if (!code) return null;
  return (
    <div className="kc-tvr-paused">
      <figure className="kc-tvr-paused__figure">
        <Qr value={code} quiet={1} className="kc-tvr-paused__qr" label="Code to pair a phone as a remote" />
        <figcaption className="kc-tvr-paused__caption"><Icon name="smartphone" size={14} strokeWidth={2.2} />Remote</figcaption>
      </figure>
    </div>
  );
}
