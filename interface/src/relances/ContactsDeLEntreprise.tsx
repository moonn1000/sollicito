import { useEffect, useRef, useState, type FormEvent } from "react";
import type {
  ClientDeLApi,
  ContactConnu,
  EntrepriseConnue,
  RefusDuContact,
  ResultatDUnContact,
  SaisieDUnContact,
} from "../api/clientDeLApi.ts";
import { Champ, sansLeChamp } from "../formulaires/Champ.tsx";

const REESSAYER = "Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.";

type Edition = { sorte: "aucune" } | { sorte: "ajout" } | { sorte: "modification"; contact: number } | { sorte: "suppression"; contact: number };

type Message = { sorte: "annonce" | "alerte"; texte: string };

/**
 * Les contacts de l'entreprise d'une candidature (#14, absorbe #30) : un nom et une adresse électronique, à qui adresser
 * une relance. On en ajoute, on les corrige, on les supprime après une confirmation affichée dans la page. Un contact
 * supprimé quitte toutes ses relances, faites comprises. Les textes s'affichent comme du texte : React les échappe.
 */
export function ContactsDeLEntreprise({
  client,
  entreprise,
  contacts,
  remplacer,
  quandChanges,
}: {
  client: ClientDeLApi;
  entreprise: EntrepriseConnue;
  /** Les contacts lus ; « echec » s'ils n'ont pas pu l'être, rien tant qu'ils ne le sont pas. */
  contacts: ContactConnu[] | "echec" | undefined;
  remplacer: (contacts: ContactConnu[]) => void;
  /** Prévenu après un contact modifié ou supprimé : ses relances l'affichent autrement. */
  quandChanges: () => void;
}) {
  const [edition, setEdition] = useState<Edition>({ sorte: "aucune" });
  const [message, setMessage] = useState<Message>();
  const [enCours, setEnCours] = useState(false);
  const suppressionEnCours = useRef(false);
  const affichee = useRef(true);
  const titre = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    affichee.current = true;
    return () => {
      affichee.current = false;
    };
  }, []);

  const liste = Array.isArray(contacts) ? contacts : [];

  // Relit les contacts, par exemple après un refus : la liste montrée n'était plus à jour.
  async function relireLesContacts() {
    try {
      const relus = await client.listerLesContacts(entreprise.id);
      if (affichee.current) {
        remplacer(relus);
      }
    } catch {
      // La liste montrée reste ; le message dit déjà ce qui a échoué.
    }
  }

  async function supprimer(contact: ContactConnu) {
    // Un ref, et non l'état : deux clics dans le même rendu ne suppriment qu'une fois.
    if (suppressionEnCours.current) {
      return;
    }

    suppressionEnCours.current = true;
    setEnCours(true);
    setMessage(undefined);
    try {
      await client.supprimerUnContact(contact.id);
      if (affichee.current) {
        remplacer(liste.filter((autre) => autre.id !== contact.id));
        setEdition({ sorte: "aucune" });
        setMessage({ sorte: "annonce", texte: `Contact supprimé : ${contact.nom}.` });
        quandChanges();
        titre.current?.focus();
      }
    } catch {
      if (affichee.current) {
        setMessage({ sorte: "alerte", texte: `Le contact n'a pas pu être supprimé. ${REESSAYER}` });
        void relireLesContacts();
      }
    } finally {
      suppressionEnCours.current = false;
      if (affichee.current) {
        setEnCours(false);
      }
    }
  }

  return (
    <section aria-labelledby="titre-des-contacts">
      <h3 id="titre-des-contacts" ref={titre} tabIndex={-1}>
        Contacts de {entreprise.nom}
      </h3>
      {contacts === undefined && <p>Chargement des contacts…</p>}
      {contacts === "echec" && (
        <p className="alerte" role="alert">
          Les contacts n'ont pas pu être chargés. Fermez Sollicito, puis relancez-le.
        </p>
      )}
      {Array.isArray(contacts) && contacts.length === 0 && <p>Aucun contact pour cette entreprise.</p>}
      {liste.length > 0 && (
        <ul className="contacts">
          {liste.map((contact) => (
            <li key={contact.id}>
              {edition.sorte === "modification" && edition.contact === contact.id ? (
                <FormulaireDUnContact
                  prefixe={`contact-${contact.id}`}
                  depart={{ nom: contact.nom, email: contact.email }}
                  libelleDuBouton="Enregistrer le contact"
                  envoyer={(saisie) => client.modifierUnContact(contact.id, saisie)}
                  quandEnregistre={(modifie) => {
                    remplacer(liste.map((autre) => (autre.id === modifie.id ? modifie : autre)));
                    setEdition({ sorte: "aucune" });
                    setMessage({ sorte: "annonce", texte: `Contact modifié : ${modifie.nom}.` });
                    quandChanges();
                    titre.current?.focus();
                  }}
                  annuler={() => setEdition({ sorte: "aucune" })}
                />
              ) : (
                <>
                  <p>
                    <span className="nom-du-contact">{contact.nom}</span> <span className="chemin">{contact.email}</span>
                  </p>
                  <div className="actions">
                    <button
                      type="button"
                      className="secondaire"
                      disabled={enCours || edition.sorte !== "aucune"}
                      aria-label={`Modifier le contact : ${contact.nom}`}
                      onClick={() => {
                        setMessage(undefined);
                        setEdition({ sorte: "modification", contact: contact.id });
                      }}
                    >
                      Modifier
                    </button>
                    <button
                      type="button"
                      className="secondaire danger"
                      disabled={enCours || edition.sorte !== "aucune"}
                      aria-label={`Supprimer le contact : ${contact.nom}`}
                      onClick={() => {
                        setMessage(undefined);
                        setEdition({ sorte: "suppression", contact: contact.id });
                      }}
                    >
                      Supprimer
                    </button>
                  </div>
                  {edition.sorte === "suppression" && edition.contact === contact.id && (
                    <div
                      className="confirmation"
                      role="group"
                      aria-labelledby={`titre-de-la-suppression-du-contact-${contact.id}`}
                      onKeyDown={(evenement) => {
                        if (evenement.key === "Escape" && !enCours) {
                          setEdition({ sorte: "aucune" });
                        }
                      }}
                    >
                      <p id={`titre-de-la-suppression-du-contact-${contact.id}`}>
                        Supprimer {contact.nom} ? La suppression est définitive : le contact n'est plus le destinataire d'aucune
                        relance, faites comprises.
                      </p>
                      <div className="actions">
                        <button type="button" className="danger" disabled={enCours} autoFocus onClick={() => void supprimer(contact)}>
                          {enCours ? "Suppression…" : "Supprimer définitivement"}
                        </button>
                        <button type="button" className="secondaire" disabled={enCours} onClick={() => setEdition({ sorte: "aucune" })}>
                          Annuler
                        </button>
                      </div>
                    </div>
                  )}
                </>
              )}
            </li>
          ))}
        </ul>
      )}
      {Array.isArray(contacts) && edition.sorte === "ajout" && (
        <FormulaireDUnContact
          prefixe="nouveau-contact"
          depart={{ nom: "", email: "" }}
          libelleDuBouton="Ajouter le contact"
          envoyer={(saisie) => client.ajouterUnContact(entreprise.id, saisie)}
          quandEnregistre={(ajoute) => {
            remplacer([...liste, ajoute]);
            setEdition({ sorte: "aucune" });
            setMessage({ sorte: "annonce", texte: `Contact ajouté : ${ajoute.nom}.` });
            titre.current?.focus();
          }}
          annuler={() => setEdition({ sorte: "aucune" })}
        />
      )}
      {Array.isArray(contacts) && edition.sorte !== "ajout" && (
        <button
          type="button"
          className="secondaire"
          disabled={enCours || edition.sorte !== "aucune"}
          onClick={() => {
            setMessage(undefined);
            setEdition({ sorte: "ajout" });
          }}
        >
          Ajouter un contact
        </button>
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
    </section>
  );
}

/** Le nom et l'adresse d'un contact, envoyés tels quels ; les refus de l'API s'affichent à côté de leur champ. */
function FormulaireDUnContact({
  prefixe,
  depart,
  libelleDuBouton,
  envoyer,
  quandEnregistre,
  annuler,
}: {
  prefixe: string;
  depart: SaisieDUnContact;
  libelleDuBouton: string;
  envoyer: (saisie: SaisieDUnContact) => Promise<ResultatDUnContact>;
  quandEnregistre: (contact: ContactConnu) => void;
  annuler: () => void;
}) {
  const [nom, setNom] = useState(depart.nom);
  const [email, setEmail] = useState(depart.email);
  const [refus, setRefus] = useState<RefusDuContact>({});
  const [envoi, setEnvoi] = useState<"pret" | "en-cours" | "echec">("pret");
  const envoiEnCours = useRef(false);
  const champs = { nom: useRef<HTMLInputElement>(null), email: useRef<HTMLInputElement>(null) };

  useEffect(() => {
    champs.nom.current?.focus();
  }, []);

  async function soumettre(evenement: FormEvent) {
    evenement.preventDefault();
    // Un ref, et non l'état : deux clics dans le même rendu n'envoient qu'une fois.
    if (envoiEnCours.current) {
      return;
    }

    envoiEnCours.current = true;
    setEnvoi("en-cours");
    try {
      const resultat = await envoyer({ nom, email });
      if (resultat.issue === "enregistre") {
        quandEnregistre(resultat.contact);
        return;
      }

      setRefus(resultat.refus);
      setEnvoi("pret");
      (resultat.refus.nom !== undefined ? champs.nom : champs.email).current?.focus();
    } catch {
      setEnvoi("echec");
    } finally {
      envoiEnCours.current = false;
    }
  }

  return (
    <form className="saisie" noValidate onSubmit={(evenement) => void soumettre(evenement)}>
      <Champ id={`${prefixe}-nom`} libelle="Nom" refus={refus.nom}>
        {(attributs) => (
          <input
            {...attributs}
            ref={champs.nom}
            type="text"
            autoComplete="off"
            value={nom}
            onChange={(evenement) => {
              setNom(evenement.target.value);
              setRefus((avant) => sansLeChamp(avant, "nom"));
            }}
          />
        )}
      </Champ>
      <Champ id={`${prefixe}-email`} libelle="Adresse électronique" aide="Une seule adresse, par exemple nom@entreprise.be." refus={refus.email}>
        {(attributs) => (
          <input
            {...attributs}
            ref={champs.email}
            type="email"
            autoComplete="off"
            spellCheck={false}
            value={email}
            onChange={(evenement) => {
              setEmail(evenement.target.value);
              setRefus((avant) => sansLeChamp(avant, "email"));
            }}
          />
        )}
      </Champ>
      <div className="actions">
        <button type="submit" disabled={envoi === "en-cours"}>
          {envoi === "en-cours" ? "Enregistrement…" : libelleDuBouton}
        </button>
        <button type="button" className="secondaire" disabled={envoi === "en-cours"} onClick={annuler}>
          Annuler
        </button>
      </div>
      {envoi === "echec" && (
        <p className="alerte" role="alert">
          Le contact n'a pas pu être enregistré. {REESSAYER}
        </p>
      )}
    </form>
  );
}
