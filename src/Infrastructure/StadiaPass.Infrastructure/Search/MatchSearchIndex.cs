namespace StadiaPass.Infrastructure.Search;

/// <summary>
/// The one index this system keeps, and the definition it is created from.
/// </summary>
/// <remarks>
/// <para>
/// Written as JSON rather than through the client's fluent builders on purpose. An index definition is a
/// document that has to be read by people as much as by code - it is what you paste into Kibana's console
/// to see what an analyzer does to a word, and what you diff when relevance changes - and the builder
/// spelling of it is neither. Everything else in this folder uses the typed client.
/// </para>
/// <para>
/// The document deliberately carries no counters. Free, held and sold change on every click somewhere in
/// the building; putting them here would mean rewriting this document on every hold and every release, for
/// numbers the seat map already reads out of PostgreSQL a moment later. What is here is what a fixture is
/// called and when it is played - which changes when somebody schedules or postpones a match, and not
/// otherwise.
/// </para>
/// </remarks>
internal static class MatchSearchIndex
{
    public const string Name = "matches";

    /// <summary>
    /// Settings and mappings, as they would be typed into <c>PUT /matches</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One shard, no replica: a replica of a single-node cluster has nowhere to live and only leaves the
    /// index yellow forever. The index is a projection of PostgreSQL and can be thrown away and rebuilt, so
    /// there is nothing here worth replicating anyway.
    /// </para>
    /// <para>
    /// The analyzer is the whole reason this is not a <c>LIKE</c> query. Turkish lowercasing knows that the
    /// capital of <c>i</c> is <c>İ</c> and of <c>ı</c> is <c>I</c>, which the invariant one gets wrong in
    /// both directions - <c>BEŞİKTAŞ</c> would otherwise not match <c>beşiktaş</c>. The apostrophe filter
    /// takes the suffix off <c>Fenerbahçe'nin</c>, asciifolding lets somebody on a keyboard without Turkish
    /// characters type <c>besiktas</c>, and the stemmer folds inflections onto one stem.
    /// </para>
    /// <para>
    /// The order of those last three is not a matter of taste, and getting it wrong was measured rather than
    /// guessed. Stemming before folding leaves the two spellings of a word on different stems: the Turkish
    /// stemmer does not recognise <c>çe</c> as a suffix, so <c>Fenerbahçe</c> survives whole and only then
    /// loses its cedilla, while a visitor typing <c>fenerbahce</c> hands the stemmer an ASCII <c>ce</c> it
    /// strips on sight - <c>fenerbahce</c> against <c>fenerbah</c>, and no match for exactly the visitor the
    /// folding was added for. Folding first puts both spellings through the stemmer as the same word.
    /// Stopwords stay ahead of the folding, because the <c>_turkish_</c> list is written in Turkish.
    /// </para>
    /// <para>
    /// The <c>.prefix</c> sub-fields carry the same words with the stopwords and the stemmer left out, and
    /// they exist because a half-typed word is not a word. The Turkish stemmer is a set of rules about how
    /// Turkish words end, and applying them to something nobody has finished typing produces a different
    /// word rather than a shorter one: <c>istanb</c> comes out as <c>istanp</c>, because the rules include
    /// the consonant that hardens at the end of a syllable. No prefix of <c>istanbul</c> begins with that, so
    /// prefix matching against the stemmed field finds nothing - and finds it inconsistently, since
    /// <c>istan</c> happens to survive as <c>ista</c> and does match. Folding and lowercasing are kept, so
    /// the prefix field forgives the same missing accents the main one does.
    /// </para>
    /// <para>
    /// <c>dynamic: strict</c> because a projection that quietly grows a field is a projection nobody is
    /// reading. A document with something unmapped in it is refused, loudly, at the point it is written.
    /// </para>
    /// </remarks>
    public const string Definition =
        """
        {
          "settings": {
            "number_of_shards": 1,
            "number_of_replicas": 0,
            "analysis": {
              "filter": {
                "turkish_lowercase": { "type": "lowercase", "language": "turkish" },
                "turkish_stop": { "type": "stop", "stopwords": "_turkish_" },
                "turkish_stemmer": { "type": "stemmer", "language": "turkish" }
              },
              "analyzer": {
                "turkish_folded": {
                  "tokenizer": "standard",
                  "filter": [
                    "apostrophe",
                    "turkish_lowercase",
                    "turkish_stop",
                    "asciifolding",
                    "turkish_stemmer"
                  ]
                },
                "turkish_prefix": {
                  "tokenizer": "standard",
                  "filter": [
                    "apostrophe",
                    "turkish_lowercase",
                    "asciifolding"
                  ]
                }
              }
            }
          },
          "mappings": {
            "dynamic": "strict",
            "properties": {
              "id": { "type": "keyword", "index": false },
              "homeTeam": {
                "type": "text",
                "analyzer": "turkish_folded",
                "fields": { "prefix": { "type": "text", "analyzer": "turkish_prefix" } }
              },
              "awayTeam": {
                "type": "text",
                "analyzer": "turkish_folded",
                "fields": { "prefix": { "type": "text", "analyzer": "turkish_prefix" } }
              },
              "venueName": {
                "type": "text",
                "analyzer": "turkish_folded",
                "fields": { "prefix": { "type": "text", "analyzer": "turkish_prefix" } }
              },
              "city": {
                "type": "text",
                "analyzer": "turkish_folded",
                "fields": {
                  "keyword": { "type": "keyword" },
                  "prefix": { "type": "text", "analyzer": "turkish_prefix" }
                }
              },
              "category": {
                "type": "text",
                "analyzer": "turkish_folded",
                "fields": {
                  "keyword": { "type": "keyword" },
                  "prefix": { "type": "text", "analyzer": "turkish_prefix" }
                }
              },
              "kickOffUtc": { "type": "date" },
              "status": { "type": "keyword" }
            }
          }
        }
        """;
}
