# Altinn.Register.Contracts

`Altinn.Register.Contracts` is a .NET class library that contains strongly-typed models used to serialize and deserialize data exchanged with the Altinn Register APIs. These models are intended to provide a stable and reusable contract layer for services integrating with Altinn's register endpoints.

## Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Installation](#installation)
- [Usage](#usage)
- [Contributing](#contributing)
- [License](#license)

## Overview

Altinn provides various APIs to retrieve information about organizations, persons, party relationships, and other entities. `Altinn.Register.Contracts` defines POCOs (Plain Old CLR Objects) to map these JSON responses, allowing easier integration and testing of your client code.

This project does **not** contain any business logic or HTTP client code. It only contains data models.

## Features

- Clean, documented C# models for use with JSON (de)serialization.
- Compatible with `System.Text.Json`.
- Follows Altinn's current API schemas.

## Installation

You can install this library via NuGet:

```bash
dotnet add package Altinn.Register.Contracts
```

Or via the Package Manager Console:

```powershell
Install-Package Altinn.Register.Contracts
```

## Usage

Here's a basic example of how you might use this package with an HTTP client:

```csharp
using System.Net.Http;
using System.Text.Json;
using Altinn.Register.Contracts;

using var client = new HttpClient();
client.BaseAddress = new Uri("BASE_ADDRESS");
using var req = new HttpRequestMessage(HttpMethod.Get, $"register/api/v2/parties/666f1fd3-ca28-467c-9cc1-807b51b7586f");
using var response = await client.SendAsync(req);
response.EnsureSuccessStatusCode();

var party = response.Content.ReadFromJsonAsync<Party>(JsonSerializerOptions.Web);

Console.WriteLine($"Organization: {party.DisplayName}");
```

## Contributing

Contributions are welcome! Please follow these steps:

1. Fork the repository.
2. Create a new branch: `git checkout -b feature/your-feature`.
3. Make your changes and add unit tests if applicable.
4. Open a pull request describing your changes.

## License

This project is licensed under the MIT License.
