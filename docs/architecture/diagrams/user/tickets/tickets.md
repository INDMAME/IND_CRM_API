# User flow: tickets

Technical source: [tickets-sequence.md](../../technical/tickets/tickets-sequence.md)

This is the user-level version of the technical ticket diagram. It follows the
same operations, but uses business terms and explains technical words in
parentheses.

```mermaid
sequenceDiagram
  autonumber
  participant User as Usuario
  participant Screen as Pantalla de tickets
  participant System as Sistema CRM<br/>(aplicacion que coordina)
  participant Check as Validacion<br/>(comprueba permisos)
  participant Ax as Axapta<br/>(sistema donde se guardan datos)
  participant Files as Archivos<br/>(imagenes de tickets)
  participant Ocr as Lectura de imagen<br/>(OCR: extrae texto)
  participant Ai as IA<br/>(ayuda a proponer datos)

  Note over User,Ax: Listar y abrir tickets
    User->>Screen: Busca tickets disponibles
    Screen->>System: Pide la lista de tickets
    System->>Check: Comprueba usuario, empresa<br/>y permisos
    Check->>Ax: Consulta tickets
    Ax-->>System: Devuelve lista
    System-->>Screen: Tickets encontrados
    Screen-->>User: Muestra la lista

    User->>Screen: Abre un ticket
    Screen->>System: Pide el detalle
    System->>Ax: Consulta el ticket
    Ax-->>System: Devuelve datos del ticket
    System-->>Screen: Detalle del ticket
    Screen-->>User: Muestra el ticket

  Note over User,Ax: Crear, editar y cambiar lineas
    User->>Screen: Crea un ticket
    Screen->>System: Envia datos del ticket
    System->>Ax: Guarda el ticket
    Ax-->>System: Ticket creado
    System-->>Screen: Confirmacion o aviso de error

    User->>Screen: Edita el ticket
    Screen->>System: Envia cambios
    System->>Ax: Actualiza el ticket
    Ax-->>System: Resultado de actualizacion
    System-->>Screen: Confirmacion o aviso de error

    User->>Screen: Cambia lineas del ticket
    Screen->>System: Envia lineas nuevas o cambiadas
    System->>Ax: Crea, actualiza o borra lineas
    Ax-->>System: Resultado de las lineas
    System-->>Screen: Confirmacion o aviso de error

  Note over User,Ai: Alta rapida con imagen e IA
    User->>Screen: Sube imagen del ticket<br/>y datos opcionales
    Screen->>System: Pide crear ticket rapido
    System->>Check: Comprueba usuario, empresa<br/>y permisos
    System->>Ax: Crea ticket provisional<br/>(borrador inicial)
    Ax-->>System: Identificador del ticket
    System->>Files: Guarda la imagen
    System->>Files: Crea acceso temporal<br/>(enlace de lectura limitado)
    System->>Ocr: Lee la imagen del recibo
    Ocr-->>System: Datos detectados en la imagen
    System->>Ai: Ordena los datos detectados
    Ai-->>System: Propuesta de cabecera y lineas
    System->>Ax: Aplica la propuesta de IA
    Ax-->>System: Ticket finalizado o para revisar
    opt Vincular a una hoja existente
      System->>Ax: Agrega el ticket a la hoja
      Ax-->>System: Resultado de vinculacion
    end
    System-->>Screen: Resultado con pasos completados
    Screen-->>User: Muestra el ticket creado

  Note over User,Ax: Aplicar IA o vincular despues
    User->>Screen: Pide aplicar IA a un ticket
    Screen->>System: Envia solicitud de IA
    System->>Files: Lee la imagen guardada
    System->>Ocr: Extrae texto de la imagen
    System->>Ai: Prepara una propuesta
    System->>Ax: Guarda la propuesta en el ticket
    Ax-->>System: Resultado de guardado
    System-->>Screen: Confirmacion o aviso de error

    User->>Screen: Vincula tickets a una hoja
    Screen->>System: Envia solicitud de vinculacion
    System->>Ax: Busca o vincula tickets
    Ax-->>System: Resultado de vinculacion
    System-->>Screen: Confirmacion o lista actualizada
```

## User-level explanation

The user can list, open, create, update, and link tickets. The AI-assisted
parts use OCR, which means reading text from an image, and IA, which proposes
structured ticket data that the system can save or leave for manual review.
