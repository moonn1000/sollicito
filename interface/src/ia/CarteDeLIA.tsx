import { useEffect, useRef, useState } from "react";
import type { ClientDeLApi } from "../api/clientDeLApi.ts";

/**
 * La carte « Connecter une IA ? » du tableau de bord (#43, ADR-0016) : le premier écran que promettent la spec de la V1
 * et le README. Elle se montre tant qu'aucun choix n'est enregistré. « Choisir une IA » mène à la section « IA » des
 * réglages ; « Continuer sans IA » enregistre « Aucune IA », et la carte ne revient plus. Elle ne bloque rien : le
 * tableau de bord se lit dessous. Un état illisible, ou des réglages illisibles en entier, ne la montrent pas.
 */
export function CarteDeLIA({ client, choisirUneIA }: { client: ClientDeLApi; choisirUneIA?: () => void }) {
  const [visible, setVisible] = useState(false);
  const [envoi, setEnvoi] = useState<"pret" | "en-cours">("pret");
  const [alerte, setAlerte] = useState<string>();
  const envoiEnCours = useRef(false);
  const affichee = useRef(true);

  useEffect(() => {
    affichee.current = true;
    client.lireLEtatDeLIA().then(
      (etat) => {
        if (affichee.current) {
          setVisible(!etat.choisi && etat.fichierLisible);
        }
      },
      () => {
        // Sans état, rien à proposer : la section des réglages dira ce qui ne se lit pas.
      },
    );
    return () => {
      affichee.current = false;
    };
  }, [client]);

  async function continuerSansIA() {
    if (envoiEnCours.current) {
      return;
    }

    envoiEnCours.current = true;
    setEnvoi("en-cours");
    setAlerte(undefined);
    try {
      const resultat = await client.choisirLeFournisseurDIA("Aucun");
      if (!affichee.current) {
        return;
      }

      if (resultat.issue === "fait") {
        setVisible(false);
      } else {
        setAlerte(resultat.motif);
      }
    } catch {
      if (affichee.current) {
        setAlerte("Le choix n'a pas pu être enregistré. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.");
      }
    } finally {
      envoiEnCours.current = false;
      if (affichee.current) {
        setEnvoi("pret");
      }
    }
  }

  if (!visible) {
    return null;
  }

  return (
    <section className="carte-de-l-ia" aria-labelledby="titre-de-la-carte-de-l-ia">
      <h3 id="titre-de-la-carte-de-l-ia">Connecter une IA ?</h3>
      <p>
        Facultative : Sollicito fonctionne aussi sans. Vous pouvez choisir un fournisseur en ligne, avec votre propre clé, ou un
        modèle qui tourne sur votre ordinateur.
      </p>
      <div className="actions">
        {choisirUneIA !== undefined && (
          <button type="button" disabled={envoi === "en-cours"} onClick={choisirUneIA}>
            Choisir une IA
          </button>
        )}
        <button type="button" className="secondaire" disabled={envoi === "en-cours"} onClick={() => void continuerSansIA()}>
          Continuer sans IA
        </button>
      </div>
      {alerte !== undefined && (
        <p className="alerte" role="alert">
          {alerte}
        </p>
      )}
    </section>
  );
}
