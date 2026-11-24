import { useEffect, useRef, useState, type FormEvent } from "react";
import type { ClientDeLApi, EtatDeLIA, FournisseurDIA } from "../api/clientDeLApi.ts";
import { Champ } from "../formulaires/Champ.tsx";

type Chargement = { etape: "chargement" } | { etape: "pret"; etat: EtatDeLIA } | { etape: "echec" };

/** Les fournisseurs proposés, dans l'ordre de l'écran. */
export const FOURNISSEURS: readonly { valeur: FournisseurDIA; libelle: string }[] = [
  { valeur: "Aucun", libelle: "Aucune IA" },
  { valeur: "Claude", libelle: "Claude, d'Anthropic, avec votre clé" },
  { valeur: "OpenAI", libelle: "OpenAI, avec votre clé" },
  { valeur: "Ollama", libelle: "Un modèle local, avec Ollama" },
];

const LIBELLES_DES_CLES = { Claude: "Clé d'API de Claude", OpenAI: "Clé d'API d'OpenAI" } as const;

const ECHEC_DU_CHOIX = "Le choix n'a pas pu être enregistré. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.";
const ECHEC_DE_LA_CLE = "La clé n'a pas pu être enregistrée. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.";
const ECHEC_DU_RETRAIT = "La clé n'a pas pu être retirée. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.";

function prendUneCle(fournisseur: FournisseurDIA): fournisseur is "Claude" | "OpenAI" {
  return fournisseur === "Claude" || fournisseur === "OpenAI";
}

/**
 * La section « IA » de l'écran des réglages (#43, ADR-0016) : choisir son fournisseur d'IA, ou aucun, et enregistrer sa
 * clé. La clé s'écrit une fois, dans un champ masqué qui se vide après l'envoi ; la page ne la relit jamais, et l'écran
 * dit seulement qu'une clé est enregistrée, à remplacer ou à retirer. Changer de fournisseur efface la clé, après une
 * confirmation dans la page. Un choix ou une clé illisibles se disent, et Sollicito continue sans IA.
 */
export function SectionDeLIA({ client, prendreLeFocus = false }: { client: ClientDeLApi; prendreLeFocus?: boolean }) {
  const [chargement, setChargement] = useState<Chargement>({ etape: "chargement" });
  const [choix, setChoix] = useState<FournisseurDIA>("Aucun");
  const [confirmation, setConfirmation] = useState<"aucune" | "changement" | "retrait">("aucune");
  const [remplacement, setRemplacement] = useState(false);
  const [cle, setCle] = useState("");
  const [refus, setRefus] = useState<string[]>();
  const [envoi, setEnvoi] = useState<"pret" | "en-cours">("pret");
  const [message, setMessage] = useState<{ sorte: "annonce" | "alerte"; texte: string }>();
  const titre = useRef<HTMLHeadingElement>(null);
  const champ = useRef<HTMLInputElement>(null);
  const envoiEnCours = useRef(false);
  const affichee = useRef(true);

  function adopter(etat: EtatDeLIA) {
    setChargement({ etape: "pret", etat });
    setChoix(etat.fournisseur);
  }

  function lire() {
    setChargement({ etape: "chargement" });
    client.lireLEtatDeLIA().then(
      (etat) => {
        if (affichee.current) {
          adopter(etat);
        }
      },
      () => {
        if (affichee.current) {
          setChargement({ etape: "echec" });
        }
      },
    );
  }

  useEffect(() => {
    affichee.current = true;
    lire();
    return () => {
      affichee.current = false;
    };
  }, [client]);

  useEffect(() => {
    if (prendreLeFocus) {
      titre.current?.focus();
    }
    // Au montage seulement : le focus suit l'arrivée depuis la carte du tableau de bord.
  }, []);

  // Une écriture à la fois : un ref, et non l'état, pour que deux clics dans le même rendu n'envoient qu'une fois.
  async function envoyer<T>(operation: () => Promise<T>, suite: (resultat: T) => void, echec: string) {
    if (envoiEnCours.current) {
      return;
    }

    envoiEnCours.current = true;
    setEnvoi("en-cours");
    setMessage(undefined);
    try {
      const resultat = await operation();
      if (affichee.current) {
        suite(resultat);
      }
    } catch {
      if (affichee.current) {
        setMessage({ sorte: "alerte", texte: echec });
      }
    } finally {
      envoiEnCours.current = false;
      if (affichee.current) {
        setEnvoi("pret");
      }
    }
  }

  function choisir(evenement: FormEvent) {
    evenement.preventDefault();
    if (chargement.etape !== "pret") {
      return;
    }

    if (choix !== chargement.etat.fournisseur && chargement.etat.cle === "Enregistree") {
      setConfirmation("changement");
      return;
    }

    void enregistrerLeChoix();
  }

  function enregistrerLeChoix() {
    setConfirmation("aucune");
    return envoyer(
      () => client.choisirLeFournisseurDIA(choix),
      (resultat) => {
        if (resultat.issue === "fait") {
          adopter(resultat.etat);
          setRemplacement(false);
          setMessage({ sorte: "annonce", texte: "Choix enregistré." });
        } else {
          setMessage({ sorte: "alerte", texte: resultat.motif });
        }
      },
      ECHEC_DU_CHOIX,
    );
  }

  function enregistrerLaCle(evenement: FormEvent) {
    evenement.preventDefault();
    const saisie = cle;
    // Le champ se vide après l'envoi, quelle qu'en soit l'issue : la page ne garde pas la clé.
    setCle("");
    setRefus(undefined);
    void envoyer(
      () => client.enregistrerLaCleDIA(saisie),
      (resultat) => {
        if (resultat.issue === "enregistree") {
          adopter(resultat.etat);
          setRemplacement(false);
        } else if (resultat.issue === "refusee") {
          setRefus(resultat.refus);
          champ.current?.focus();
        } else {
          setMessage({ sorte: "alerte", texte: resultat.motif });
        }
      },
      ECHEC_DE_LA_CLE,
    );
  }

  function retirerLaCle() {
    setConfirmation("aucune");
    void envoyer(
      () => client.retirerLaCleDIA(),
      (resultat) => {
        if (resultat.issue === "fait") {
          adopter(resultat.etat);
        } else {
          setMessage({ sorte: "alerte", texte: resultat.motif });
        }
      },
      ECHEC_DU_RETRAIT,
    );
  }

  const enCours = envoi === "en-cours";

  return (
    <section className="sauvegardes reglage-de-l-ia" aria-labelledby="titre-de-l-ia" aria-busy={enCours}>
      <h3 id="titre-de-l-ia" ref={titre} tabIndex={-1}>
        IA
      </h3>
      {chargement.etape === "chargement" && <p>Chargement du réglage…</p>}
      {chargement.etape === "echec" && (
        <div>
          <p className="alerte" role="alert">
            L'état de l'IA n'a pas pu être lu. Réessayez.
          </p>
          <div className="actions">
            <button type="button" onClick={lire}>
              Réessayer
            </button>
          </div>
        </div>
      )}
      {chargement.etape === "pret" && (
        <>
          <p>Facultative : Sollicito fonctionne aussi sans IA.</p>
          {!chargement.etat.fichierLisible && (
            <p className="alerte" role="alert">
              Les réglages n'ont pas pu être relus : Sollicito continue sans IA. Choisissez de nouveau le dossier des sauvegardes,
              qui les réécrit, puis l'IA.
            </p>
          )}
          {chargement.etat.fichierLisible && !chargement.etat.reglageLisible && (
            <p className="alerte" role="alert">
              Le choix de l'IA n'a pas pu être relu : Sollicito continue sans IA. Choisissez de nouveau.
            </p>
          )}
          <form className="saisie" noValidate onSubmit={choisir}>
            <fieldset className="choix-du-fournisseur" disabled={enCours}>
              <legend>Fournisseur</legend>
              {FOURNISSEURS.map((fournisseur) => (
                <label key={fournisseur.valeur}>
                  <input
                    type="radio"
                    name="fournisseur-de-l-ia"
                    value={fournisseur.valeur}
                    checked={choix === fournisseur.valeur}
                    onChange={() => {
                      setChoix(fournisseur.valeur);
                      setConfirmation("aucune");
                      setMessage(undefined);
                    }}
                  />
                  {fournisseur.libelle}
                </label>
              ))}
            </fieldset>
            {confirmation === "changement" ? (
              <div
                className="confirmation"
                role="group"
                aria-labelledby="titre-du-changement-de-fournisseur"
                onKeyDown={(evenement) => {
                  if (evenement.key === "Escape" && !enCours) {
                    setConfirmation("aucune");
                  }
                }}
              >
                <p id="titre-du-changement-de-fournisseur">Changer de fournisseur efface la clé enregistrée.</p>
                <div className="actions">
                  <button type="button" className="danger" disabled={enCours} onClick={() => void enregistrerLeChoix()}>
                    Changer et effacer la clé
                  </button>
                  <button type="button" className="secondaire" disabled={enCours} autoFocus onClick={() => setConfirmation("aucune")}>
                    Annuler
                  </button>
                </div>
              </div>
            ) : (
              <div className="actions">
                <button type="submit" disabled={enCours}>
                  {enCours ? "Enregistrement…" : "Enregistrer ce choix"}
                </button>
              </div>
            )}
          </form>
          {chargement.etat.reglageLisible && chargement.etat.fournisseur === "Ollama" && (
            <p>Le modèle tourne sur votre ordinateur, sans clé.</p>
          )}
          {chargement.etat.reglageLisible && prendUneCle(chargement.etat.fournisseur) && (
            <div className="cle-de-l-ia">
              {chargement.etat.cle === "Illisible" && (
                <p className="alerte" role="alert">
                  La clé enregistrée ne peut pas être relue sur ce poste, par exemple après le passage à un autre ordinateur ou à
                  un autre compte Windows. Sollicito continue sans IA : saisissez-la de nouveau.
                </p>
              )}
              {chargement.etat.cle === "Aucune" && <p>Aucune clé n'est enregistrée : Sollicito continue sans IA.</p>}
              {chargement.etat.cle === "Enregistree" && !remplacement && (
                <>
                  <p role="status">Une clé est enregistrée.</p>
                  {confirmation === "retrait" ? (
                    <div
                      className="confirmation"
                      role="group"
                      aria-labelledby="titre-du-retrait-de-la-cle"
                      onKeyDown={(evenement) => {
                        if (evenement.key === "Escape" && !enCours) {
                          setConfirmation("aucune");
                        }
                      }}
                    >
                      <p id="titre-du-retrait-de-la-cle">Retirer la clé l'efface de ce poste.</p>
                      <div className="actions">
                        <button type="button" className="danger" disabled={enCours} onClick={retirerLaCle}>
                          Retirer la clé
                        </button>
                        <button type="button" className="secondaire" disabled={enCours} autoFocus onClick={() => setConfirmation("aucune")}>
                          Annuler
                        </button>
                      </div>
                    </div>
                  ) : (
                    <div className="actions">
                      <button
                        type="button"
                        disabled={enCours}
                        onClick={() => {
                          setMessage(undefined);
                          setRemplacement(true);
                        }}
                      >
                        Remplacer la clé
                      </button>
                      <button
                        type="button"
                        className="secondaire"
                        disabled={enCours}
                        onClick={() => {
                          setMessage(undefined);
                          setConfirmation("retrait");
                        }}
                      >
                        Retirer la clé
                      </button>
                    </div>
                  )}
                </>
              )}
              {(chargement.etat.cle !== "Enregistree" || remplacement) && (
                <form className="saisie" noValidate onSubmit={enregistrerLaCle}>
                  <Champ
                    id="cle-de-l-ia"
                    libelle={LIBELLES_DES_CLES[chargement.etat.fournisseur]}
                    aide="Sollicito la chiffre sur ce poste, pour votre seul compte Windows, et ne l'affiche plus ensuite."
                    refus={refus}
                  >
                    {(attributs) => (
                      <input
                        {...attributs}
                        ref={champ}
                        type="password"
                        autoComplete="off"
                        autoCapitalize="off"
                        spellCheck={false}
                        autoFocus={remplacement}
                        value={cle}
                        onChange={(evenement) => {
                          setCle(evenement.target.value);
                          setRefus(undefined);
                        }}
                      />
                    )}
                  </Champ>
                  <div className="actions">
                    <button type="submit" disabled={enCours}>
                      {enCours ? "Enregistrement…" : "Enregistrer la clé"}
                    </button>
                    {remplacement && (
                      <button
                        type="button"
                        className="secondaire"
                        disabled={enCours}
                        onClick={() => {
                          setRemplacement(false);
                          setCle("");
                          setRefus(undefined);
                        }}
                      >
                        Annuler
                      </button>
                    )}
                  </div>
                </form>
              )}
            </div>
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
        </>
      )}
    </section>
  );
}
