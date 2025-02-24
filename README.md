# OOSWebScraper

## Overview
**OOSWebScraper** is a web scraping tool built with **C# .NET 8** that automates the process of retrieving and reporting on out-of-stock products, recently added items, and other relevant product details from the **RGS** and **DSS** websites. The scraper runs daily and updates the report accordingly.

## Features
- Scrapes product availability data from **RGS** and **DSS** websites.
- Identifies **out-of-stock** items and **recently added** products.
- Updates and generates a daily report.
- Uses **PuppeteerSharp** for headless browser automation.
- Well-documented code for easy maintenance and scalability.

## Tech Stack
- **Language:** C#
- **Framework:** .NET 8
- **Automation Library:** PuppeteerSharp
- **Data Processing:** LINQ, System.IO for file handling
- **Logging:** Built-in .NET logging

## Installation
1. Clone the repository:
   ```sh
   git clone https://github.com/your-repo/OOSWebScraper.git
   cd OOSWebScraper
   ```
2. Install dependencies:
   ```sh
   dotnet restore
   ```
3. Build the project:
   ```sh
   dotnet build
   ```

## Usage
1. Run the scraper manually:
   ```sh
   dotnet run
   ```
2. Configure it as a scheduled task (Windows Task Scheduler or Cron Job) to run daily.

## Configuration
- Update the `appsettings.json` or environment variables to modify URLs and scraping parameters.
- Ensure **Google Chrome** or **Chromium** is installed for PuppeteerSharp.

## Code Documentation
The project is **well-documented** with inline comments explaining the logic and methods used. Feel free to explore the source code for further insights.

## Author
[Sam Espinoza] - [sespinoza@excelligence.com]
