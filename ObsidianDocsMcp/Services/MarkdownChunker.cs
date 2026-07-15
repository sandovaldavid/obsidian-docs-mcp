using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Markdig.Syntax;
using ObsidianDocsMcp.Models;

namespace ObsidianDocsMcp.Services;

/// <summary>
/// Splits markdown documents into logical section chunks for indexing. Pure and stateless, so
/// chunking behavior can be tested and evolved independently of the indexing pipeline.
/// </summary>
public static class MarkdownChunker
{
    /// <summary>Hard safety limit: sections longer than this are split into "(Part N)" chunks.</summary>
    public const int MaxChunkChars = 2000;

    /// <summary>
    /// Splits markdown into logical sections using Markdig's parsed heading spans (levels 1-4),
    /// so headings inside fenced code blocks are correctly ignored and setext headings are
    /// detected too — cases a hand-rolled line scan misses.
    /// </summary>
    public static List<SectionChunk> Segment(string markdown, string relativePath, string title, string sourceName)
    {
        var chunks = new List<SectionChunk>();
        int chunkIndex = 0;

        void FlushChunk(string header, string rawContent)
        {
            var trimmed = rawContent.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return;
            }

            // Safety split for very long sections, same 2000-char budget as before.
            var needsSplit = trimmed.Length > MaxChunkChars;
            for (int offset = 0; offset < trimmed.Length; offset += MaxChunkChars)
            {
                var partLength = Math.Min(MaxChunkChars, trimmed.Length - offset);
                var part = trimmed.Substring(offset, partLength);
                var partHeader = needsSplit ? $"{header} (Part {(offset / MaxChunkChars) + 1})" : header;
                // Relative paths can exist in both documentation sources (for example en/Home.md).
                // Keep their identities distinct so one source cannot replace the other's chunk.
                var chunkId = GenerateHash($"{sourceName}_{relativePath}_{chunkIndex}_{partHeader}");
                chunks.Add(new SectionChunk
                {
                    Id = chunkId,
                    FilePath = relativePath,
                    Title = $"{sourceName} > {title}",
                    Header = partHeader,
                    Content = part
                });
                chunkIndex++;
            }
        }

        var document = Markdig.Markdown.Parse(markdown);
        var headings = document.Descendants<HeadingBlock>()
            .Where(h => h.Level <= 4)
            .OrderBy(h => h.Span.Start)
            .ToList();

        if (headings.Count == 0)
        {
            FlushChunk("General", markdown);
            return chunks;
        }

        if (headings[0].Span.Start > 0)
        {
            FlushChunk("General", markdown[..headings[0].Span.Start]);
        }

        for (int i = 0; i < headings.Count; i++)
        {
            var heading = headings[i];
            // For setext headings (underlined with ===/---), Span covers both the text line and
            // the underline, so only take the first line to get just the heading text.
            var rawHeadingSpan = markdown.Substring(heading.Span.Start, heading.Span.Length);
            var firstLineEnd = rawHeadingSpan.IndexOfAny(['\r', '\n']);
            var headingText = (firstLineEnd >= 0 ? rawHeadingSpan[..firstLineEnd] : rawHeadingSpan).TrimStart('#').Trim();

            var contentStart = heading.Span.End + 1;
            var contentEnd = i + 1 < headings.Count ? headings[i + 1].Span.Start : markdown.Length;
            contentEnd = Math.Min(contentEnd, markdown.Length);

            if (contentStart >= contentEnd)
            {
                continue;
            }

            var sectionContent = markdown[contentStart..contentEnd];
            FlushChunk(string.IsNullOrWhiteSpace(headingText) ? "General" : headingText, sectionContent);
        }

        return chunks;
    }

    private static string GenerateHash(string input)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = SHA256.HashData(inputBytes);
        return Convert.ToHexString(hashBytes).ToLower();
    }
}
