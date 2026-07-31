# Access2Pay API Documentation

Input and output reference for Access2Pay playground APIs. Auth uses the `X-API-Key` header.

## What each API does

| API | Purpose |
|-----|---------|
| **InitiateProcess** | OCR the invoice file, upload it to our backend, and return the OCR JSON payload. |
| **GetProcessTickets** | Get process tickets (list or filter by fields such as `referenceNo`). |
| **RouteProcessTicket** | Update a ticket by `referenceNo` — set status to values like `pending`, `processed`, or `on hold`. |

Typical flow: **InitiateProcess** → **GetProcessTickets** → **RouteProcessTicket**.

---

## Authentication

| Name | In | Required | Description |
|------|----|----------|-------------|
| `X-API-Key` | header | yes | Playground API key |

| HTTP | Meaning |
|------|---------|
| `200` | Success |
| `400` | Validation or processing error (`EncryptOutput`) |
| `401` | Missing or invalid API key |

Success body shape:

```json
{
  "id": 1,
  "output": "...",
  "encryptOutput": null
}
```

Error body shape:

```json
{
  "id": 0,
  "output": null,
  "encryptOutput": "Error message"
}
```

---

## 1. InitiateProcess

Send an invoice file. The API runs OCR on the file, uploads it to our backend, and returns the OCR result as Access2Pay JSON in `output`.

| | |
|--|--|
| **Method** | `POST` |
| **Endpoint** | `/api/Access2Pay/InitiateProcess` |
| **Content-Type** | `multipart/form-data` |

### Input

| Name | In | Required | Description |
|------|----|----------|-------------|
| `X-API-Key` | header | yes | Playground API key |
| `file` | form-data | yes | Invoice PDF or image to OCR and upload |
| `storageCallbackUrl` | form-data | no | If set, OCR payload JSON is POSTed to this URL before the response returns |

### Output (`output`)

OCR JSON payload (vendor, invoice, line items, `referenceNo`, source document, etc.).

```json
{
  "referenceNo": "REQ-71",
  "submission": {
    "submittedFrom": "user@example.com",
    "emailSubject": "Vendor Name",
    "receivedFileName": "invoice.pdf",
    "submittedAtUtc": "2026-07-30T13:41:55.429Z"
  },
  "sourceDocument": {
    "fileName": "invoice.pdf",
    "mimeType": "application/pdf",
    "storageProvider": "blob",
    "location": "https://...",
    "externalId": ""
  },
  "Vendor": {
    "vendorId": "",
    "company": "Vendor Name",
    "address1": "...",
    "city": "",
    "state": "",
    "zip": "",
    "country": "",
    "email": "",
    "contactName": "",
    "glid": ""
  },
  "Invoice": {
    "documentType": "Invoice",
    "fidNumber": null,
    "invoiceNumber": "INV-001",
    "invoiceDate": "2026-07-24",
    "poNumber": "PO-1002",
    "poDate": "",
    "deliveryDocNumber": null,
    "deliveryDocDate": null,
    "currency": "CAD",
    "buyer": {
      "billToName": "...",
      "billToAddress": "...",
      "shipToName": null,
      "shipToAddress": null
    },
    "amounts": {
      "grossAmount": "$840.00",
      "taxAmount": "$109.20",
      "discount": null,
      "charge": null,
      "roundOff": null,
      "netTotal": "$949.20"
    },
    "paymentTerms": "Net 30",
    "notes": "",
    "remittance": {
      "bankName": "...",
      "bankAccount": "",
      "bankAccountNumber": ""
    },
    "lineItems": [
      {
        "lineNumber": 1,
        "itemNumber": null,
        "description": "...",
        "quantity": "1",
        "unitOfMeasure": "",
        "rate": "100.00",
        "lineAmount": "$100.00",
        "metadata": []
      }
    ]
  }
}
```

---

## 2. GetProcessTickets

Get process tickets — browse all tickets or filter (for example by `referenceNo` or status).

| | |
|--|--|
| **Method** | `POST` |
| **Endpoint** | `/api/Access2Pay/GetProcessTickets` |
| **Content-Type** | `application/json` |

### Input

| Name | In | Required | Description |
|------|----|----------|-------------|
| `X-API-Key` | header | yes | Playground API key |
| body | JSON | yes | Browse / filter query for process tickets |

**Sample body**

```json
{
  "sortBy": { "criteria": "id", "order": "DESC" },
  "filterBy": [],
  "currentPage": 1,
  "itemsPerPage": 0,
  "mode": "browse"
}
```

**filterBy example**

```json
[
  {
    "groupCondition": "",
    "filters": [
      {
        "criteria": "referenceNo",
        "condition": "IS_EQUALS_TO",
        "value": "EZ-00012"
      }
    ]
  }
]
```

### Output (`output`)

Process ticket list / browse result (JSON in `output`).

---

## 3. RouteProcessTicket

Update a process ticket by `referenceNo` only (no record id in the path). Use this to change ticket status — for example `pending`, `processed`, or `on hold`.

| | |
|--|--|
| **Method** | `PUT` |
| **Endpoint** | `/api/Access2Pay/RouteProcessTicket` |
| **Content-Type** | `application/json` |

### Input

| Name | In | Required | Description |
|------|----|----------|-------------|
| `X-API-Key` | header | yes | Playground API key |
| body | JSON | yes | Must include `referenceNo`; set `transactionStatus` (and any other fields) to update |

**Status examples for `transactionStatus`**

| Value | Meaning |
|-------|---------|
| `pending` | Ticket is waiting / in progress |
| `processed` | Ticket is completed |
| `on hold` | Ticket is paused / on hold |

**Sample body**

```json
{
  "referenceNo": "EZ-00012",
  "transactionStatus": "processed"
}
```

Other status examples:

```json
{ "referenceNo": "EZ-00012", "transactionStatus": "pending" }
```

```json
{ "referenceNo": "EZ-00012", "transactionStatus": "on hold" }
```

### Output (`output`)

Update result from Access2Pay (JSON in `output`).

---

# V6 Design System Specification

## 1. Project Overview
The V6 Design System is built on a "Flat-Focus" architecture, prioritizing a "weightless" user experience. It avoids blocking overlays (modals/drawers) in favor of contextual anchors and inline expansions. The aesthetic is modern, clean, and premium, utilizing a Radix-inspired color palette and responsive typography.

---

## 2. Color System
### Backgrounds
*   **Primary Page Background**: `#ffffff` (`--surface-primary`) / `#fdfcfd` (`--gray-1`)
*   **Card / Surface Background**: `#ffffff` (`--surface-raised`)
*   **Muted Surface**: `#faf9fb` (`--gray-2`)

### Brand Colors
*   **Primary (Purple)**: `#9333ea` (`--primary-9`) - Used for main actions and branding.
*   **Secondary (Cyan)**: `#00bcd4` (`--secondary-9`) - Used for highlights and accents.

### Grayscale Scale
*   **Gray-3 (Border)**: `#f2eff3`
*   **Gray-8 (Muted/Icon)**: `#bcbac7`
*   **Gray-10 (Text Muted)**: `#84828e`
*   **Gray-11 (Text Secondary)**: `#65636d`
*   **Gray-13 (Text Primary)**: `#211f26`

### Feedback Palette
*   **Success**: `#30a46c` (`--green-9`) / Light: `#e6f6eb` (`--green-3`)
*   **Error**: `#e5484d` (`--red-9`) / Light: `#feebec` (`--red-3`)
*   **Warning**: `#f76b15` (`--orange-9`) / Light: `#ffefd6` (`--orange-3`)

---

## 3. Typography System
*   **Primary Font**: `'Inter', sans-serif` (Utility and UI)
*   **Secondary Font**: `'Poppins', sans-serif` (Headlines and Branding)

| Level | Size (px/rem) | Weight | Color Variable |
| :--- | :--- | :--- | :--- |
| **Page Title** | `clamp(13px, 1.2vw, 16px)` | 700 (Bold) | `--gray-13` |
| **Subtitle** | `13px` / `0.8125rem` | 600 (Semibold) | `--gray-11` |
| **Body Text** | `14px` / `0.875rem` | 450 (Regular) | `--gray-13` |
| **Caption** | `11px` / `0.6875rem` | 450 (Regular) | `--gray-10` |

---

## 4. Border & Radius System
*   **Primary Border**: `1px solid #f2eff3` (`--gray-3`)
*   **Base Radius**: `5px` (`--radius`)
*   **Card/Container Radius**: `12px` (`rounded-xl`)
*   **Pill Radius**: `9999px` (`rounded-full`)

---

## 5. Shadow & Elevation
*   **Sm Shadow**: `0 1px 2px 0 rgb(0 0 0 / 0.05)`
*   **Pill Shadow**: `0 10px 25px -5px rgba(124, 58, 237, 0.15)`
*   **Focus Ring**: `0 0 0 2px rgba(147, 51, 234, 0.1)`

---

## 6. Core CSS Implementation
```css
:root {
  /* Brand Tokens */
  --primary: #9333ea;
  --secondary: #00bcd4;
  --surface: #ffffff;
  --gray-bg: #faf9fb;

  /* Typography */
  --font-main: 'Inter', sans-serif;
  --font-head: 'Poppins', sans-serif;
  --text-primary: #211f26;
  --text-muted: #84828e;

  /* Spacing & Borders */
  --radius-sm: 5px;
  --radius-xl: 12px;
  --border-color: #f2eff3;
}

/* Base Typography */
body {
  font-family: var(--font-main);
  color: var(--text-primary);
  background-color: var(--surface);
  -webkit-font-smoothing: antialiased;
}

h1, .page-title {
  font-family: var(--font-head);
  font-size: clamp(13px, 1.2vw, 16px);
  font-weight: 700;
  letter-spacing: -0.01em;
}

/* Card Styles */
.card-base {
  background: var(--surface);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-xl);
  box-shadow: 0 1px 3px rgba(0,0,0,0.05);
  transition: box-shadow 0.2s ease;
}

.card-base:hover {
  box-shadow: 0 4px 6px rgba(0,0,0,0.07);
}

/* Button Styles */
.btn-primary {
  background: var(--primary);
  color: #fff;
  border-radius: var(--radius-sm);
  padding: 8px 16px;
  font-weight: 500;
  transition: all 0.2s;
}

.btn-primary:active {
  transform: scale(0.98);
}

.btn-secondary {
  background: transparent;
  color: var(--primary);
  border: 1px solid var(--primary);
  border-radius: var(--radius-sm);
  padding: 8px 16px;
}

/* Radius Utilities */
.rounded-xl { border-radius: var(--radius-xl); }
.rounded-sm { border-radius: var(--radius-sm); }
```
