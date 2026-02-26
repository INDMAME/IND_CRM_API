# Regla de versionado Postman

Cuando el usuario pida "genera un archivo postman" o solicite crear una nueva version del proyecto:
- Usa siempre la ultima coleccion existente como base (la version mas reciente).
- Genera una nueva coleccion incrementando la version (V2 -> V3 -> V4, etc.).
- Conserva versiones anteriores sin sobrescribir.
- En la nueva version manten y utiliza las variables globales existentes, y agrega los nuevos endpoints implementados recientemente.
- Guarda el archivo en .codex/Postman con el nombre "IND_CRM_API V{n}.postman_collection.json".
- Actualiza el nombre de la collection y el _postman_id.
