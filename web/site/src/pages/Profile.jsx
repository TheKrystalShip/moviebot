import { Icon, SettingsRow, SettingsSection, ThemePicker } from "@thekrystalship/krystal-ui";
import { signOut, useSession } from "../session.js";
import { navigate } from "../router.js";
import { PageHead } from "../components/Film.jsx";

/** Who is signed in, how the site looks for them, and the way out. */
export function Profile() {
  const session = useSession();
  const user = session && session.user;

  return (
    <div className="kc-page kc-profile">
      <PageHead title={user ? user.name : "Profile"} sub={user ? "@" + user.username : null} />
      <SettingsSection icon="palette" title="Appearance">
        <ThemePicker />
      </SettingsSection>
      <SettingsSection icon="user" title="Account">
        <SettingsRow icon="log-in" title="Signed in with Discord" sub="Your sign-in lasts a few hours, then Discord is asked again.">
          <button type="button" className="kc-btn" onClick={() => { signOut(); navigate("/signin", { replace: true }); }}>
            <Icon name="log-out" size={16} />Sign out
          </button>
        </SettingsRow>
      </SettingsSection>
    </div>
  );
}
