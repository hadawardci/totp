# totp-api

Minimal API em .NET 10 para gerar códigos TOTP (2FA) via HTTP.

## Requisitos

- [Docker](https://docs.docker.com/get-docker/) e Docker Compose

## Configuração

Copie o arquivo de exemplo e preencha com o segredo Base32 do seu serviço:

```bash
cp .env.example .env
```

Edite `.env`:

```env
TOTP_SECRET=SUA_CHAVE_BASE32_AQUI
```

> A chave Base32 é fornecida pelo serviço no momento em que você ativa o 2FA (geralmente exibida como "chave manual" ou "setup key").

## Executar

```bash
docker compose up --build
```

A API ficará disponível em `http://localhost:8080`.

## Endpoints

### `GET /code`

Retorna o código TOTP do segredo configurado em `TOTP_SECRET`.

```bash
curl http://localhost:8080/code
```

```json
{
  "code": "123456",
  "generated_at": "2026-05-06T12:00:00+00:00"
}
```

---

### `GET /code/{secret}`

Retorna o código TOTP para qualquer chave Base32 informada na URL.

```bash
curl http://localhost:8080/code/JBSWY3DPEHPK3PXP
```

```json
{
  "code": "123456",
  "generated_at": "2026-05-06T12:00:00+00:00"
}
```

---

### `GET /otpauth?uri={uri}`

Aceita um URI completo no formato `otpauth://totp/` e extrai automaticamente o segredo, algoritmo, dígitos e período.

| Parâmetro | Descrição |
|-----------|-----------|
| `uri` | URI otpauth completo (deve ser URL-encoded) |

```bash
curl -G "http://localhost:8080/otpauth" \
  --data-urlencode "uri=otpauth://totp/Conta:usuario@email.com?secret=JBSWY3DPEHPK3PXP&digits=6&algorithm=SHA1&issuer=Conta&period=30"
```

```json
{
  "code": "123456",
  "generated_at": "2026-05-06T12:00:00+00:00"
}
```

> Este é o formato exportado por serviços como AWS, GitHub, Google, entre outros, quando você ativa o 2FA via aplicativo autenticador.

---

### `GET /base32/{data}`

Decodifica um payload de migração do Google Authenticator (`otpauth-migration://offline?data=...`) e retorna os códigos TOTP de todas as contas contidas no export.

| Parâmetro | Descrição |
|-----------|-----------|
| `data` | Valor do parâmetro `data` extraído do URI de migração |

```bash
curl "http://localhost:8080/base32/CmoKKLzKCNZLwxvZ..."
```

```json
[
  {
    "issuer": "Amazon Web Services",
    "name": "usuario@empresa.com",
    "code": "123456",
    "generated_at": "2026-05-06T12:00:00+00:00"
  }
]
```

> O payload de migração é gerado pelo Google Authenticator em **Exportar contas**. O valor do parâmetro `data` da URL gerada é o que deve ser passado neste endpoint.

## Licença

MIT
