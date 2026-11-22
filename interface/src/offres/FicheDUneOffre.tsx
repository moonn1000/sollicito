import { useEffect, useRef, useState } from "react";
import { estIntrouvable, type ClientDeLApi, type FicheDUneOffre as Fiche } from "../api/clientDeLApi.ts";
import { afficherLInstant } from "../candidatures/affichage.ts";
import { LienExterne } from "../liens/LienExterne.tsx";
import { afficherLeJour } from "../relances/affichage.ts";
import { decrireLeDoublon, LIBELLES_DES_SOURCES } from "./affichage.ts";

type Chargement = { etape: "chargement" } | { etape: "prete"; fiche: Fiche } | { etape: "introuvable" } | { etape: "echec" };

/**
 * La fiche d'une offre (#36) : ses champs, l'adresse de son annonce, par le seul lien externe de l'interface, son texte,
 * tel quel, et ses doublons possibles (règle 3), relus à chaque ouverture. Elle mène à la correction de ses champs.
 * Les textes s'affichent comme du texte, jamais comme du HTML : React les échappe, et les sauts de ligne du texte
 * s'affichent par le style.
 */
export function FicheDUneOffre({
  client,
  offre,
  annonce,
  revenir,
  modifier,
  ouvrirUneOffre,
}: {
  client: ClientDeLApi;
  offre: number;
  /** Ce qui vient de se passer, par exemple une offre enregistrée. */
  annonce?: string;
  /** Mène au tableau de bord. */
  revenir: () => void;
  /** Mène à la correction des champs de l'offre. */
  modifier: () => void;
  /** Ouvre la fiche d'un doublon possible. */
  ouvrirUneOffre: (offre: number) => void;
}) {
  const [chargement, setChargement] = useState<Chargement>({ etape: "chargement" });
  const titre = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    let affichee = true;
    client.lireUneOffre(offre).then(
      (fiche) => {
        if (affichee) {
          setChargement({ etape: "prete", fiche });
        }
      },
      (erreur: unknown) => {
        if (affichee) {
          setChargement({ etape: estIntrouvable(erreur) ? "introuvable" : "echec" });
        }
      },
    );
    return () => {
      affichee = false;
    };
  }, [client, offre]);

  useEffect(() => {
    titre.current?.focus();
  }, [chargement.etape]);

  if (chargement.etape !== "prete") {
    return (
      <section aria-labelledby="titre-de-l-offre">
        <h2 id="titre-de-l-offre" ref={titre} tabIndex={-1}>
          Offre
        </h2>
        {chargement.etape === "chargement" && <p role="status">Chargement de l'offre…</p>}
        {chargement.etape === "introuvable" && <p className="alerte" role="alert">Cette offre n'existe pas.</p>}
        {chargement.etape === "echec" && (
          <p className="alerte" role="alert">
            L'offre n'a pas pu être lue : fermez Sollicito, puis relancez-le.
          </p>
        )}
        <div className="actions">
          <button type="button" className="secondaire" onClick={revenir}>
            Revenir au tableau de bord
          </button>
        </div>
      </section>
    );
  }

  const { fiche } = chargement;
  return (
    <section aria-labelledby="titre-de-l-offre" className="fiche fiche-d-une-offre">
      {annonce !== undefined && (
        <p className="annonce" role="status">
          {annonce}
        </p>
      )}
      <h2 id="titre-de-l-offre" ref={titre} tabIndex={-1}>
        {fiche.poste}
      </h2>
      <dl className="resume">
        <dt>Entreprise</dt>
        <dd>{fiche.entreprise?.nom ?? "Entreprise non indiquée"}</dd>
        {fiche.lieu !== null && (
          <>
            <dt>Lieu</dt>
            <dd>{fiche.lieu}</dd>
          </>
        )}
        {fiche.publieeLe !== null && (
          <>
            <dt>Publiée le</dt>
            <dd>{afficherLeJour(fiche.publieeLe)}</dd>
          </>
        )}
        <dt>Ajoutée le</dt>
        <dd>{afficherLInstant(fiche.ajouteeLe)}</dd>
        <dt>Source</dt>
        <dd>{LIBELLES_DES_SOURCES[fiche.source]}</dd>
        {fiche.adresse !== null && (
          <>
            <dt>Annonce</dt>
            <dd>
              <LienExterne adresse={fiche.adresse} />
            </dd>
          </>
        )}
      </dl>
      <div className="actions">
        <button type="button" onClick={modifier}>
          Modifier l'offre
        </button>
        <button type="button" className="secondaire" onClick={revenir}>
          Revenir au tableau de bord
        </button>
      </div>
      {fiche.doublonsPossibles.length > 0 && (
        <section aria-labelledby="titre-des-doublons" className="doublons-possibles">
          <h3 id="titre-des-doublons">Doublons possibles</h3>
          <p>Une autre offre du même poste, dans la même entreprise, est enregistrée :</p>
          <ul>
            {fiche.doublonsPossibles.map((doublon) => (
              <li key={doublon.id}>
                <span>{decrireLeDoublon(doublon)}</span>{" "}
                <button type="button" className="secondaire" onClick={() => ouvrirUneOffre(doublon.id)}>
                  Voir cette offre
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}
      {fiche.texte !== null && (
        <section aria-labelledby="titre-du-texte">
          <h3 id="titre-du-texte">Texte de l'offre</h3>
          <div className="texte-de-l-offre">{fiche.texte}</div>
        </section>
      )}
    </section>
  );
}
