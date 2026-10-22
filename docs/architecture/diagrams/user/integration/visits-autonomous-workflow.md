# Visitas: trabajo autónomo y copia en Axapta

Estado: funcionamiento propuesto. Axapta es el sistema central de gestión que recibirá una copia de las visitas. Fuente técnica: [arquitectura destino](../../technical/integration/visits-autonomous-target.md) y [guardado y entrega](../../technical/integration/visits-outbox-sequence.md).

Exportaciones: [SVG](../../../assets/user/integration/visits-autonomous-workflow.svg) · [PNG](../../../assets/user/integration/visits-autonomous-workflow.png).

```mermaid
flowchart TB
  START["Iniciar sesion corporativa"] --> ACCESS["Seleccionar empresa autorizada"]
  ACCESS --> VIEW["Consultar clientes e historial<br/>desde la base propia"]
  VIEW --> EDIT["Crear o editar visita y asistentes"]
  EDIT --> SAVE["Guardar todo en la base propia"]
  SAVE --> ACK["La persona recibe confirmacion<br/>sin esperar a Axapta"]
  SAVE --> PENDING["Preparar envio en segundo plano"]
  PENDING --> UP{"Axapta disponible<br/>y datos admitidos?"}
  UP -->|"Si"| SENT["Confirmar copia en Axapta"]
  UP -->|"Fallo temporal"| RETRY["Reintentar sin crear duplicados"]
  RETRY --> PENDING
  UP -->|"Dato incompatible"| REVIEW["Mostrar incidencia para revision"]
  IMPORT["Importacion periodica<br/>de maestros e historico autorizado"] --> VIEW
  RULE["Las visitas migradas se editan en la nueva web.<br/>La importacion respeta esos cambios."] --- SAVE
```

El estado de envío se mostrará por separado del guardado y del estado de negocio. Una incidencia en Axapta no debe hacer perder la visita guardada. Las modificaciones inesperadas en la copia Axapta requieren revisión. La frecuencia de actualización y el retraso aceptable siguen pendientes de acordar.
