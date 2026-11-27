import { useEffect, useRef, useState, type FormEvent } from "react";
import type { ClientDeLApi, ReglageDuDelai } from "../api/clientDeLApi.ts";
import { Champ, sansLeChamp } from "../formulaires/Champ.tsx";
import { DELAI_MAXIMUM, DELAI_MINIMUM } from "./affichage.ts";

type Chargement = { etape: "chargement" } | { etape: "pret"; reglage: ReglageDuDelai } | { etape: "echec" };

const DELAI_HORS_BORNES = "Le délai d'une relance compte de 1 à 365 jours.";

/**
 * La section « Relances » de l'écran des réglages (#14, ADR-0011) : le délai par défaut d'une relance, en jours depuis
 * l'envoi, gardé dans les réglages de l'application, hors de la base. Il ne sert qu'aux relances planifiées ensuite.
 * Un délai enregistré qui ne se relit pas, ou un fichier des réglages illisible, se dit à l'écran.
 */
export function SectionDesRelances({
  client,
  reecritures = 0,
}: {
  client: ClientDeLApi;
  /** Le nombre de réécritures de reglages.json depuis l'ouverture de l'écran : à chacune, la section relit le délai. */
  reecritures?: number;
}) {
  const [chargement, setChargement] = useState<Chargement>({ etape: "chargement" });
  const [delai, setDelai] = useState("");
  const [refus, setRefus] = useState<{ delai?: string[] }>({});
  const [envoi, setEnvoi] = useState<"pret" | "en-cours" | "echec">("pret");
  const [message, setMessage] = useState<{ sorte: "annonce" | "alerte"; texte: string }>();
  const champ = useRef<HTMLInputElement>(null);
  const envoiEnCours = useRef(false);
  const affichee = useRef(true);
  // Le dernier délai lu ou enregistré : une relecture ne remplace pas un délai saisi depuis.
  const delaiLu = useRef("");

  function adopter(reglage: ReglageDuDelai) {
    const avant = delaiLu.current;
    const lu = String(reglage.delaiParDefaut);
    delaiLu.current = lu;
    setChargement({ etape: "pret", reglage });
    setDelai((saisi) => (saisi === avant ? lu : saisi));
  }

  useEffect(() => {
    affichee.current = true;
    client.lireLeReglageDesRelances().then(
      (reglage) => {
        if (affichee.current) {
          adopter(reglage);
        }
      },
      () => {
        if (affichee.current) {
          setChargement({ etape: "echec" });
        }
      },
    );
    return () => {
      affichee.current = false;
    };
  }, [client]);

  useEffect(() => {
    if (reecritures === 0) {
      return;
    }

    client.lireLeReglageDesRelances().then(
      (reglage) => {
        if (affichee.current) {
          // Un refus d'écriture sur le fichier d'avant ne vaut plus.
          setMessage(undefined);
          adopter(reglage);
        }
      },
      () => {
        // La section garde ce qu'elle montrait.
      },
    );
  }, [reecritures]);

  async function enregistrer(evenement: FormEvent) {
    evenement.preventDefault();
    // Un ref, et non l'état : deux clics dans le même rendu n'envoient qu'une fois.
    if (envoiEnCours.current) {
      return;
    }

    setMessage(undefined);
    const jours = /^\d{1,3}$/u.test(delai.trim()) ? Number(delai.trim()) : Number.NaN;
    if (!(Number.isInteger(jours) && jours >= DELAI_MINIMUM && jours <= DELAI_MAXIMUM)) {
      setRefus({ delai: [DELAI_HORS_BORNES] });
      champ.current?.focus();
      return;
    }

    envoiEnCours.current = true;
    setEnvoi("en-cours");
    try {
      const resultat = await client.changerLeDelaiParDefaut(jours);
      if (!affichee.current) {
        return;
      }

      setEnvoi("pret");
      if (resultat.issue === "change") {
        delaiLu.current = String(resultat.reglage.delaiParDefaut);
        setChargement({ etape: "pret", reglage: resultat.reglage });
        setDelai(delaiLu.current);
        setMessage({ sorte: "annonce", texte: `Délai par défaut enregistré : ${resultat.reglage.delaiParDefaut} jours.` });
      } else if (resultat.issue === "refuse") {
        setRefus({ delai: resultat.refus });
        champ.current?.focus();
      } else {
        setMessage({ sorte: "alerte", texte: resultat.motif });
      }
    } catch {
      if (affichee.current) {
        setEnvoi("echec");
      }
    } finally {
      envoiEnCours.current = false;
    }
  }

  return (
    <section className="sauvegardes" aria-labelledby="titre-des-relances-reglees">
      <h3 id="titre-des-relances-reglees">Relances</h3>
      {chargement.etape === "chargement" && <p>Chargement du réglage…</p>}
      {chargement.etape === "echec" && (
        <p className="alerte" role="alert">
          Le délai par défaut n'a pas pu être lu. Fermez Sollicito, puis relancez-le.
        </p>
      )}
      {chargement.etape === "pret" && (
        <>
          {!chargement.reglage.fichierLisible && (
            <p className="alerte" role="alert">
              Les réglages n'ont pas pu être relus : le délai d'usine de 7 jours s'applique. Choisissez de nouveau le dossier des
              sauvegardes, qui les réécrit, puis réglez le délai.
            </p>
          )}
          {chargement.reglage.fichierLisible && !chargement.reglage.delaiLisible && (
            <p className="alerte" role="alert">
              Le délai enregistré n'a pas pu être relu : le délai d'usine de 7 jours s'applique, jusqu'à ce que vous en enregistriez un.
            </p>
          )}
          <form className="saisie" noValidate onSubmit={(evenement) => void enregistrer(evenement)}>
            <Champ
              id="delai-par-defaut"
              libelle="Délai par défaut d'une relance, en jours depuis l'envoi"
              aide="De 1 à 365 jours. Il ne change pas les relances déjà planifiées."
              refus={refus.delai}
            >
              {(attributs) => (
                <input
                  {...attributs}
                  ref={champ}
                  type="number"
                  inputMode="numeric"
                  min={DELAI_MINIMUM}
                  max={DELAI_MAXIMUM}
                  value={delai}
                  onChange={(evenement) => {
                    setDelai(evenement.target.value);
                    setRefus((avant) => sansLeChamp(avant, "delai"));
                  }}
                />
              )}
            </Champ>
            <div className="actions">
              <button type="submit" disabled={envoi === "en-cours"}>
                {envoi === "en-cours" ? "Enregistrement…" : "Enregistrer le délai"}
              </button>
            </div>
            {envoi === "echec" && (
              <p className="alerte" role="alert">
                Le délai n'a pas pu être enregistré. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.
              </p>
            )}
            {message?.sorte === "annonce" && (
              <p className="annonce" role="status">
                {message.texte}
              </p>
            )}
            {message?.sorte === "alerte" && (
              <p className="alerte" role="alert">
                {message.texte}
              </p>
            )}
          </form>
        </>
      )}
    </section>
  );
}
