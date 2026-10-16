DELETE FROM register.external_role_assignment_event
WHERE "source" = 'cra'
  AND "identifier" in ('namsmannen-tvangsfullbyrdelse', 'skatteetaten-innkreving', 'statens-innkrevingssentral-gjeldsordning-betalingsavtaler');

DELETE FROM register.external_role_assignment
WHERE "source" = 'cra'
  AND "identifier" in ('namsmannen-tvangsfullbyrdelse', 'skatteetaten-innkreving', 'statens-innkrevingssentral-gjeldsordning-betalingsavtaler');

DELETE FROM register.external_role_definition
WHERE "source" = 'cra'
  AND "identifier" in ('namsmannen-tvangsfullbyrdelse', 'skatteetaten-innkreving', 'statens-innkrevingssentral-gjeldsordning-betalingsavtaler');
