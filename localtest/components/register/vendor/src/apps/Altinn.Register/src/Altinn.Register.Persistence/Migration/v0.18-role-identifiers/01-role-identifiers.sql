-- Har som registreringsenhet
UPDATE register.external_role_definition erd
SET identifier = 'hovedenhet'
WHERE erd.source = 'ccr' AND erd.identifier = 'bedr';

-- Har som registreringsenhet
UPDATE register.external_role_definition erd
SET identifier = 'ikke-naeringsdrivende-hovedenhet'
WHERE erd.source = 'ccr' AND erd.identifier = 'aafy';

-- Inngår i foretaksgruppe med
UPDATE register.external_role_definition erd
SET identifier = 'foretaksgruppe-med'
WHERE erd.source = 'ccr' AND erd.identifier = 'fgrp';

-- Hovedforetak
UPDATE register.external_role_definition erd
SET identifier = 'hovedforetak'
WHERE erd.source = 'ccr' AND erd.identifier = 'hfor';

-- Helseforetak
UPDATE register.external_role_definition erd
SET identifier = 'helseforetak'
WHERE erd.source = 'ccr' AND erd.identifier = 'hlse';

-- Innehaver
UPDATE register.external_role_definition erd
SET identifier = 'innehaver'
WHERE erd.source = 'ccr' AND erd.identifier = 'innh';

-- Har som datter i konsern
UPDATE register.external_role_definition erd
SET identifier = 'konsern-datter'
WHERE erd.source = 'ccr' AND erd.identifier = 'kdat';

-- Har som grunnlag for konsern
UPDATE register.external_role_definition erd
SET identifier = 'konsern-grunnlag'
WHERE erd.source = 'ccr' AND erd.identifier = 'kgrl';

-- Inngår i kirkelig fellesråd
UPDATE register.external_role_definition erd
SET identifier = 'kirkelig-fellesraad'
WHERE erd.source = 'ccr' AND erd.identifier = 'kirk';

-- Har som mor i konsern
UPDATE register.external_role_definition erd
SET identifier = 'konsern-mor'
WHERE erd.source = 'ccr' AND erd.identifier = 'kmor';

-- Komplementar
UPDATE register.external_role_definition erd
SET identifier = 'komplementar'
WHERE erd.source = 'ccr' AND erd.identifier = 'komp';

-- Kontaktperson
UPDATE register.external_role_definition erd
SET identifier = 'kontaktperson'
WHERE erd.source = 'ccr' AND erd.identifier = 'kont';

-- Inngår i kontorfellesskap
UPDATE register.external_role_definition erd
SET identifier = 'kontorfelleskapmedlem'
WHERE erd.source = 'ccr' AND erd.identifier = 'ktrf';

-- Styrets leder
UPDATE register.external_role_definition erd
SET identifier = 'styreleder'
WHERE erd.source = 'ccr' AND erd.identifier = 'lede';

-- Styremedlem
UPDATE register.external_role_definition erd
SET identifier = 'styremedlem'
WHERE erd.source = 'ccr' AND erd.identifier = 'medl';

-- Nestleder
UPDATE register.external_role_definition erd
SET identifier = 'nestleder'
WHERE erd.source = 'ccr' AND erd.identifier = 'nest';

-- Observator
UPDATE register.external_role_definition erd
SET identifier = 'observator'
WHERE erd.source = 'ccr' AND erd.identifier = 'obs';

-- Er særskilt oppdelt enhet til
UPDATE register.external_role_definition erd
SET identifier = 'saerskilt-oppdelt-enhet'
WHERE erd.source = 'ccr' AND erd.identifier = 'opmv';

-- Organisasjonsledd i offentlig sektor
UPDATE register.external_role_definition erd
SET identifier = 'organisasjonsledd-offentlig-sektor'
WHERE erd.source = 'ccr' AND erd.identifier = 'orgl';

-- Prokura i fellesskap
UPDATE register.external_role_definition erd
SET identifier = 'prokurist-fellesskap'
WHERE erd.source = 'ccr' AND erd.identifier = 'pofe';

-- Prokura hver for seg
UPDATE register.external_role_definition erd
SET identifier = 'prokurist-hver-for-seg'
WHERE erd.source = 'ccr' AND erd.identifier = 'pohv';

-- Prokura
UPDATE register.external_role_definition erd
SET identifier = 'prokurist'
WHERE erd.source = 'ccr' AND erd.identifier = 'prok';

-- Er revisoradresse for
UPDATE register.external_role_definition erd
SET identifier = 'revisoradressat'
WHERE erd.source = 'ccr' AND erd.identifier = 'read';

-- Forestår avvikling
UPDATE register.external_role_definition erd
SET identifier = 'forestaar-avvikling'
WHERE erd.source = 'ccr' AND erd.identifier = 'avkl';

-- Deltaker med delt ansvar
UPDATE register.external_role_definition erd
SET identifier = 'deltaker-delt-ansvar'
WHERE erd.source = 'ccr' AND erd.identifier = 'dtpr';

-- Deltaker med fullt ansvar
UPDATE register.external_role_definition erd
SET identifier = 'deltaker-fullt-ansvar'
WHERE erd.source = 'ccr' AND erd.identifier = 'dtso';

-- Eierkommune
UPDATE register.external_role_definition erd
SET identifier = 'eierkommune'
WHERE erd.source = 'ccr' AND erd.identifier = 'eikm';

-- Inngår i felles- registrering
UPDATE register.external_role_definition erd
SET identifier = 'felles-registrert-med'
WHERE erd.source = 'ccr' AND erd.identifier = 'femv';

-- Er regnskapsforeradresse for
UPDATE register.external_role_definition erd
SET identifier = 'regnskapsforeradressat'
WHERE erd.source = 'ccr' AND erd.identifier = 'rfad';

-- Sameiere
UPDATE register.external_role_definition erd
SET identifier = 'sameier'
WHERE erd.source = 'ccr' AND erd.identifier = 'sam';

-- Signatur i fellesskap
UPDATE register.external_role_definition erd
SET identifier = 'signerer-fellesskap'
WHERE erd.source = 'ccr' AND erd.identifier = 'sife';

-- Signatur
UPDATE register.external_role_definition erd
SET identifier = 'signerer'
WHERE erd.source = 'ccr' AND erd.identifier = 'sign';

-- Signatur hver for seg
UPDATE register.external_role_definition erd
SET identifier = 'signerer-hver-for-seg'
WHERE erd.source = 'ccr' AND erd.identifier = 'sihv';

-- Er frivillig registrert utleiebygg for
UPDATE register.external_role_definition erd
SET identifier = 'utleiebygg'
WHERE erd.source = 'ccr' AND erd.identifier = 'utbg';

-- Varamedlem
UPDATE register.external_role_definition erd
SET identifier = 'varamedlem'
WHERE erd.source = 'ccr' AND erd.identifier = 'vara';

-- Er virksomhet drevet i fellesskap av
UPDATE register.external_role_definition erd
SET identifier = 'virksomhet-fellesskap-drifter'
WHERE erd.source = 'ccr' AND erd.identifier = 'vife';

-- Utfyller MVA-oppgaver
UPDATE register.external_role_definition erd
SET identifier = 'mva-utfyller'
WHERE erd.source = 'ccr' AND erd.identifier = 'mvau';

-- Signerer MVA-oppgaver
UPDATE register.external_role_definition erd
SET identifier = 'mva-signerer'
WHERE erd.source = 'ccr' AND erd.identifier = 'mvag';

-- Kontaktperson i kommune
UPDATE register.external_role_definition erd
SET identifier = 'kontaktperson-kommune'
WHERE erd.source = 'ccr' AND erd.identifier = 'komk';

-- Kontaktperson for NUF
UPDATE register.external_role_definition erd
SET identifier = 'kontaktperson-nuf'
WHERE erd.source = 'ccr' AND erd.identifier = 'knuf';

-- Kontaktperson i Adm. enhet - offentlig sektor
UPDATE register.external_role_definition erd
SET identifier = 'kontaktperson-ados'
WHERE erd.source = 'ccr' AND erd.identifier = 'kemn';

-- Revisor registrert i revisorregisteret
UPDATE register.external_role_definition erd
SET identifier = 'kontaktperson-revisor'
WHERE erd.source = 'ccr' AND erd.identifier = 'sreva';

-- Bestyrende reder
UPDATE register.external_role_definition erd
SET identifier = 'bestyrende-reder'
WHERE erd.source = 'ccr' AND erd.identifier = 'best';

-- Regnskapsforer
UPDATE register.external_role_definition erd
SET identifier = 'regnskapsforer'
WHERE erd.source = 'ccr' AND erd.identifier = 'regn';

-- Norsk representant for utenlandsk enhet
UPDATE register.external_role_definition erd
SET identifier = 'norsk-representant'
WHERE erd.source = 'ccr' AND erd.identifier = 'repr';

-- Revisor
UPDATE register.external_role_definition erd
SET identifier = 'revisor'
WHERE erd.source = 'ccr' AND erd.identifier = 'revi';

-- Daglig leder
UPDATE register.external_role_definition erd
SET identifier = 'daglig-leder'
WHERE erd.source = 'ccr' AND erd.identifier = 'dagl';

-- Bostyrer
UPDATE register.external_role_definition erd
SET identifier = 'bostyrer'
WHERE erd.source = 'ccr' AND erd.identifier = 'bobe';

-- Stifter
UPDATE register.external_role_definition erd
SET identifier = 'stifter'
WHERE erd.source = 'ccr' AND erd.identifier = 'stft';

-- Den personlige konkursen angår
UPDATE register.external_role_definition erd
SET identifier = 'personlige-konkurs'
WHERE erd.source = 'ccr' AND erd.identifier = 'kenk';

-- Konkursdebitor
UPDATE register.external_role_definition erd
SET identifier = 'konkursdebitor'
WHERE erd.source = 'ccr' AND erd.identifier = 'kdeb';

-- Varamedlem i partiets utovende organ
UPDATE register.external_role_definition erd
SET identifier = 'parti-organ-varamedlem'
WHERE erd.source = 'ccr' AND erd.identifier = 'hvar';

-- Nestleder i partiets utovende organ
UPDATE register.external_role_definition erd
SET identifier = 'parti-organ-nestleder'
WHERE erd.source = 'ccr' AND erd.identifier = 'hnst';

-- Styremedlem i partiets utovende organ
UPDATE register.external_role_definition erd
SET identifier = 'parti-organ-styremedlem'
WHERE erd.source = 'ccr' AND erd.identifier = 'hmdl';

-- Leder i partiets utovende organ
UPDATE register.external_role_definition erd
SET identifier = 'parti-organ-leder'
WHERE erd.source = 'ccr' AND erd.identifier = 'hled';

-- Elektronisk signeringsrett
UPDATE register.external_role_definition erd
SET identifier = 'elektronisk-signeringsrettig'
WHERE erd.source = 'ccr' AND erd.identifier = 'esgr';

-- Skal fusjoneres med
UPDATE register.external_role_definition erd
SET identifier = 'fusjonsovertaker'
WHERE erd.source = 'ccr' AND erd.identifier = 'fusj';

-- Skal fisjoneres med
UPDATE register.external_role_definition erd
SET identifier = 'fisjonsovertaker'
WHERE erd.source = 'ccr' AND erd.identifier = 'fisj';

-- Tildeler av elektronisk signeringsrett
UPDATE register.external_role_definition erd
SET identifier = 'elektronisk-signeringsrett-tildeler'
WHERE erd.source = 'ccr' AND erd.identifier = 'etdl';

-- Administrativ enhet - offentlig sektor
UPDATE register.external_role_definition erd
SET identifier = 'administrativ-enhet-offentlig-sektor'
WHERE erd.source = 'ccr' AND erd.identifier = 'ados';

-- Forretningsforer
UPDATE register.external_role_definition erd
SET identifier = 'forretningsforer'
WHERE erd.source = 'ccr' AND erd.identifier = 'ffor';
