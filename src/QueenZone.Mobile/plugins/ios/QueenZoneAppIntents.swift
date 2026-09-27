// Compiled into the app target by withIosAppIntents.cjs until Expo App Intents supports SDK 57.
// Public endpoints only. Never put member credentials or private content in an intent.
import AppIntents
import CoreSpotlight
import UIKit

private enum QueenZoneIntentAPI {
  static let base = "__QUEENZONE_API_BASE_URL__"

  static func get<T: Decodable>(_ path: String) async throws -> T {
    guard let url = URL(string: base + "/api/v1" + path) else { throw URLError(.badURL) }
    var request = URLRequest(url: url)
    request.timeoutInterval = 8
    let (data, response) = try await URLSession.shared.data(for: request)
    guard let response = response as? HTTPURLResponse, (200...299).contains(response.statusCode) else {
      throw URLError(.badServerResponse)
    }
    return try JSONDecoder().decode(T.self, from: data)
  }

  @MainActor static func open(_ destination: String) async {
    guard let url = URL(string: destination) else { return }
    _ = await UIApplication.shared.open(url)
  }
}

private struct QueenZoneDay: Decodable {
  let id: Int
  let title: String
  let summary: String
}

private struct QueenZoneFact: Decodable {
  let text: String
}

private struct QueenZoneSearchHit: Decodable {
  let contentType: String
  let id: Int?
  let title: String
}

struct QueenZoneTodayIntent: AppIntent {
  static var title: LocalizedStringResource = "Queen history today"
  static var description = IntentDescription("Hear a Queen history highlight for today.")
  static var openAppWhenRun = true

  func perform() async throws -> some IntentResult & ProvidesDialog {
    do {
      let day: QueenZoneDay? = try await QueenZoneIntentAPI.get("/content/on-this-day")
      guard let day, !day.summary.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
        return .result(dialog: "There is no Queen history highlight for today in QueenZone.")
      }
      await QueenZoneIntentAPI.open(day.id > 0 ? "queenzone://timeline/\(day.id)" : "queenzone://timeline")
      return .result(dialog: IntentDialog(stringLiteral: day.summary))
    } catch {
      return .result(dialog: "QueenZone history is unavailable right now. Please try again later.")
    }
  }
}

struct QueenZoneNewsIntent: AppIntent {
  static var title: LocalizedStringResource = "Latest Queen news"
  static var description = IntentDescription("Open QueenZone's latest news.")
  static var openAppWhenRun = true

  func perform() async throws -> some IntentResult & ProvidesDialog {
    await QueenZoneIntentAPI.open("queenzone://news")
    return .result(dialog: "Opening the latest Queen news in QueenZone.")
  }
}

struct QueenZoneTriviaIntent: AppIntent {
  static var title: LocalizedStringResource = "Queen trivia"
  static var description = IntentDescription("Hear one published Queen fact from QueenZone.")

  func perform() async throws -> some IntentResult & ProvidesDialog {
    do {
      let fact: QueenZoneFact? = try await QueenZoneIntentAPI.get("/content/trivia/random")
      guard let fact, !fact.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
        return .result(dialog: "QueenZone has no published fact available right now.")
      }
      // Siri speaks the published fact in the confirmation prompt. Declining
      // simply leaves the person in Siri; accepting opens the Trivia screen.
      if #available(iOS 18.0, *) {
        do {
          try await requestConfirmation(dialog: IntentDialog(stringLiteral: fact.text + " Open QueenZone Trivia for more?"))
          await QueenZoneIntentAPI.open("queenzone://trivia")
        } catch {
          // The person declined the optional navigation, not the fact itself.
        }
      }
      return .result(dialog: IntentDialog(stringLiteral: fact.text))
    } catch {
      return .result(dialog: "QueenZone trivia is unavailable right now. Please try again later.")
    }
  }
}

struct QueenZoneSearchIntent: AppIntent {
  static var title: LocalizedStringResource = "Search QueenZone"
  static var description = IntentDescription("Search QueenZone's public archive.")
  static var openAppWhenRun = true

  @Parameter(title: "Search for") var searchTerm: String

  static var parameterSummary: some ParameterSummary {
    Summary("Search QueenZone for \(\.$searchTerm)")
  }

  func perform() async throws -> some IntentResult & ProvidesDialog {
    let term = searchTerm.trimmingCharacters(in: .whitespacesAndNewlines)
    guard term.count >= 2, term.count <= 100,
      let encoded = term.addingPercentEncoding(withAllowedCharacters: .alphanumerics) else {
      return .result(dialog: "Please give me at least two words or characters to search for.")
    }
    if let page: QueenZonePage<QueenZoneSearchHit> = try? await QueenZoneIntentAPI.get("/search?q=\(encoded)&page=1&pageSize=20") {
      let exact = page.items.filter { $0.title.localizedCaseInsensitiveCompare(term) == .orderedSame }
      if exact.count == 1, let match = exact.first, let id = match.id, id > 0 {
        let destination: String?
        switch match.contentType.lowercased() {
        case "discography": destination = "queenzone://album/\(id)"
        case "timeline": destination = "queenzone://timeline/\(id)"
        case "biography": destination = "queenzone://biography/\(id)"
        default: destination = nil
        }
        if let destination {
          await QueenZoneIntentAPI.open(destination)
          return .result(dialog: IntentDialog(stringLiteral: "Opening \(match.title) in QueenZone."))
        }
      }
    }
    await QueenZoneIntentAPI.open("queenzone://search?q=\(encoded)")
    return .result(dialog: IntentDialog(stringLiteral: "Searching QueenZone for \(term)."))
  }
}

// A deliberately small public catalog. The server remains the source of truth;
// this is only a local discovery index, never a member-data cache.
@available(iOS 18.4, *)
struct QueenZoneArchiveEntity: IndexedEntity {
  static var typeDisplayRepresentation: TypeDisplayRepresentation = "QueenZone Archive"
  static var defaultQuery = QueenZoneArchiveQuery()

  let id: String
  @Property(indexingKey: \.displayName) var name: String
  let kind: String

  init(id: String, name: String, kind: String) {
    self.id = id
    self.kind = kind
    self.name = name
  }

  var displayRepresentation: DisplayRepresentation {
    DisplayRepresentation(title: "\(name)", subtitle: "QueenZone \(kind)")
  }
}

private struct QueenZonePage<T: Decodable>: Decodable {
  let items: [T]
}

private struct QueenZoneAlbumRow: Decodable {
  let albumId: Int
  let name: String
}

private struct QueenZoneTimelineRow: Decodable {
  let id: Int
  let title: String
}

private struct QueenZoneArchiveItem: Codable {
  let id: String
  let name: String
  let kind: String
}

@available(iOS 18.4, *)
private enum QueenZoneArchiveCatalog {
  private static let cacheKey = "QueenZonePublicArchiveItems"

  static func load() async throws -> [QueenZoneArchiveEntity] {
    do {
      async let albums: QueenZonePage<QueenZoneAlbumRow> = QueenZoneIntentAPI.get("/content/discography?page=1&pageSize=40")
      async let events: QueenZonePage<QueenZoneTimelineRow> = QueenZoneIntentAPI.get("/content/timeline?page=1&pageSize=40")
      let albumItems = try await albums.items.map { QueenZoneArchiveItem(id: "album:\($0.albumId)", name: $0.name, kind: "album") }
      let eventItems = try await events.items.map { QueenZoneArchiveItem(id: "event:\($0.id)", name: $0.title, kind: "history") }
      let items = albumItems + eventItems
      if let encoded = try? JSONEncoder().encode(items) {
        UserDefaults.standard.set(encoded, forKey: cacheKey)
      }
      return items.map { QueenZoneArchiveEntity(id: $0.id, name: $0.name, kind: $0.kind) }
    } catch {
      guard let data = UserDefaults.standard.data(forKey: cacheKey) else { throw error }
      return try JSONDecoder().decode([QueenZoneArchiveItem].self, from: data).map {
        QueenZoneArchiveEntity(id: $0.id, name: $0.name, kind: $0.kind)
      }
    }
  }

  static func refreshSpotlight() async {
    do {
      let entities = try await load()
      let index = CSSearchableIndex(name: "QueenZonePublicArchive")
      try await index.deleteAllSearchableItems()
      try await index.indexAppEntities(entities)
    } catch {
      // Offline and server errors must not interrupt app launch.
    }
  }
}

@available(iOS 18.4, *)
struct QueenZoneArchiveQuery: EntityStringQuery {
  func entities(for identifiers: [String]) async throws -> [QueenZoneArchiveEntity] {
    let ids = Set(identifiers)
    return try await QueenZoneArchiveCatalog.load().filter { ids.contains($0.id) }
  }

  func entities(matching string: String) async throws -> [QueenZoneArchiveEntity] {
    let term = string.trimmingCharacters(in: .whitespacesAndNewlines)
    guard term.count >= 2 else { return [] }
    return try await QueenZoneArchiveCatalog.load().filter {
      $0.name.localizedCaseInsensitiveContains(term)
    }
  }

  func suggestedEntities() async throws -> [QueenZoneArchiveEntity] {
    try await QueenZoneArchiveCatalog.load()
  }
}

@available(iOS 18.4, *)
struct QueenZoneOpenArchiveIntent: OpenIntent {
  static var title: LocalizedStringResource = "Open QueenZone archive item"
  @Parameter(title: "Archive item") var target: QueenZoneArchiveEntity

  func perform() async throws -> some IntentResult {
    let parts = target.id.split(separator: ":", maxSplits: 1)
    guard parts.count == 2, let id = Int(parts[1]), id > 0 else { return .result() }
    if parts[0] == "album" {
      await QueenZoneIntentAPI.open("queenzone://album/\(id)")
    } else if parts[0] == "event" {
      await QueenZoneIntentAPI.open("queenzone://timeline/\(id)")
    }
    return .result()
  }
}

struct QueenZoneAppShortcuts: AppShortcutsProvider {
  static var appShortcuts: [AppShortcut] {
    AppShortcut(intent: QueenZoneTodayIntent(), phrases: ["What happened today in \(.applicationName)"], shortTitle: "On This Day", systemImageName: "calendar")
    AppShortcut(intent: QueenZoneNewsIntent(), phrases: ["Show latest news in \(.applicationName)"], shortTitle: "Latest News", systemImageName: "newspaper")
    AppShortcut(intent: QueenZoneTriviaIntent(), phrases: ["Tell me a Queen fact from \(.applicationName)"], shortTitle: "Queen Trivia", systemImageName: "lightbulb")
    AppShortcut(intent: QueenZoneSearchIntent(), phrases: ["Search \(.applicationName)"], shortTitle: "Search", systemImageName: "magnifyingglass")
  }
}
