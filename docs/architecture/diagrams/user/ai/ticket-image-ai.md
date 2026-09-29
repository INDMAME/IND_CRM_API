# Flujo funcional: ticket desde imagen con IA

Fuente técnica:
[ticket-image-ai-sequence.md](../../technical/ai/ticket-image-ai-sequence.md)

Esta versión conserva los pasos del flujo técnico, pero usa etiquetas sencillas
y explica entre paréntesis los términos necesarios.

```mermaid
sequenceDiagram
  autonumber
  participant User as Usuario
  participant Screen as Pantalla de tickets
  participant Limit as Control de uso<br/>(evita el exceso de IA)
  participant System as Sistema CRM<br/>(aplicación que coordina)
  participant Check as Validación<br/>(comprueba permisos)
  participant Ax as Axapta<br/>(sistema donde se guardan datos)
  participant Store as Servicio de archivos<br/>(prepara y controla imágenes)
  participant Blob as Azure Blob<br/>(nube donde queda la imagen)
  participant Ocr as Azure OCR<br/>(lee texto de la imagen)
  participant Ai as OpenAI<br/>(ordena los datos)

  User->>Screen: Sube imagen del recibo<br/>y datos opcionales
  Screen->>Limit: Envía la solicitud de ticket rápido
  Limit->>Limit: Comprueba el límite de IA<br/>y las solicitudes simultáneas

  alt Se supera el límite de IA
    Limit-->>Screen: Mensaje de límite o reintento
    Screen-->>User: Muestra aviso
  else Solicitud permitida
    Limit->>System: Continúa la creación rápida
    System->>Check: Comprueba permisos,<br/>empresa y usuario
    Check-->>System: Solicitud permitida
    System->>System: Comprueba la imagen<br/>(formato, tipo y tamaño)
    System->>Ax: Crea ticket provisional<br/>(registro inicial)
    Ax-->>System: Devuelve identificador del ticket

    System->>Store: Pide guardar la imagen original
    Store->>Blob: Guarda la imagen en Azure Blob
    Blob-->>Store: Devuelve la ubicación del archivo
    Store-->>System: Informa dónde quedó la imagen
    System->>Ax: Guarda en el ticket<br/>la ubicación de la imagen
    Ax-->>System: Imagen asociada al ticket

    System->>Store: Pide enlace temporal<br/>(solo lectura por poco tiempo)
    Store->>Blob: Crea acceso temporal a la imagen
    Blob-->>Store: Devuelve enlace temporal
    Store-->>System: Entrega enlace temporal
    System->>Ocr: Lee el recibo desde la imagen
    Ocr-->>System: Texto y datos detectados
    System->>Ai: Pide ordenar los datos<br/>(importe, fecha, líneas)
    Ai-->>System: Borrador estructurado<br/>(propuesta editable)

    System->>System: Convierte el borrador<br/>a datos del ticket

    alt Líneas válidas
      System->>Ax: Guarda la cabecera y las líneas<br/>propuestas por la IA
      Ax-->>System: Ticket finalizado
      System->>System: Marca paso como finalizado
    else Solo la cabecera es válida
      System->>Ax: Guarda la cabecera y los datos de IA<br/>sin reemplazar líneas
      Ax-->>System: Ticket para revisión manual
      System->>System: Mantiene el ticket para revisar
    end

    opt El usuario eligió una hoja existente
      System->>Ax: Carga el ticket final
      Ax-->>System: Detalle del ticket
      System->>Ax: Agrega el ticket a la hoja
      Ax-->>System: Resultado de la vinculación
      System->>System: Marca paso como vinculado
    end

    System-->>Screen: Resultado con ticket,<br/>archivo y pasos completados
    Screen-->>User: Muestra el ticket creado
  end

  Note over System,Ai: Variante de solo borrador:<br/>la pantalla puede pedir leer una imagen<br/>sin crear el ticket final.<br/>El sistema usa la misma IA,<br/>guarda una imagen temporal en Azure Blob,<br/>la borra al terminar,<br/>y solo persiste si se pide.
```

## Explicación funcional

La persona sube la imagen de un justificante. El sistema crea un ticket
provisional, guarda la imagen, la lee con OCR y pide a la IA que ordene los
datos detectados en campos y líneas.

Si las líneas propuestas son válidas, finaliza el ticket. Si solo es válida la
cabecera, queda disponible para revisión manual. Si se eligió una hoja
existente, también puede vincularse a ella.
