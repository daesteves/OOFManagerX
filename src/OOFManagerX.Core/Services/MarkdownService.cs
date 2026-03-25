using Markdig;
using System.Text.RegularExpressions;

namespace OOFManagerX.Core.Services;

/// <summary>
/// Service for converting markdown to HTML for use in OOF messages.
/// Ensures compatibility with Outlook/Exchange automatic replies.
/// 
/// Supported formatting:
/// - **bold** or __bold__
/// - *italic* or _italic_
/// - ~~strikethrough~~
/// - [link text](url)
/// - # Headings (h1-h6)
/// - - bullet lists
/// - 1. numbered lists
/// - > blockquotes
/// - `inline code`
/// - --- horizontal rule
/// </summary>
public static partial class MarkdownService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    /// <summary>
    /// Converts markdown text to HTML suitable for Outlook/Exchange automatic replies.
    /// </summary>
    public static string ConvertToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        // Convert markdown to HTML using Markdig
        var html = Markdown.ToHtml(markdown, Pipeline);
        
        // Convert to Outlook-compatible HTML with inline styles
        html = MakeOutlookCompatible(html);
        
        // Wrap in container with base font styling
        return WrapForOutlook(html);
    }

    /// <summary>
    /// Converts standard HTML to Outlook-compatible HTML.
    /// Outlook/Exchange automatic replies require inline styles for consistent rendering.
    /// </summary>
    private static string MakeOutlookCompatible(string html)
    {
        // === Text Formatting ===
        
        // Bold: <strong> -> inline style
        html = Regex.Replace(html, @"<strong>(.*?)</strong>", 
            "<span style=\"font-weight:bold;\">$1</span>", RegexOptions.Singleline);
        html = Regex.Replace(html, @"<b>(.*?)</b>", 
            "<span style=\"font-weight:bold;\">$1</span>", RegexOptions.Singleline);
        
        // Italic: <em> -> inline style  
        html = Regex.Replace(html, @"<em>(.*?)</em>", 
            "<span style=\"font-style:italic;\">$1</span>", RegexOptions.Singleline);
        html = Regex.Replace(html, @"<i>(.*?)</i>", 
            "<span style=\"font-style:italic;\">$1</span>", RegexOptions.Singleline);
        
        // Strikethrough: <del> -> inline style
        html = Regex.Replace(html, @"<del>(.*?)</del>", 
            "<span style=\"text-decoration:line-through;\">$1</span>", RegexOptions.Singleline);
        html = Regex.Replace(html, @"<s>(.*?)</s>", 
            "<span style=\"text-decoration:line-through;\">$1</span>", RegexOptions.Singleline);
        
        // Inline code: <code> -> monospace with background
        html = Regex.Replace(html, @"<code>(.*?)</code>", 
            "<span style=\"font-family:Consolas,Monaco,monospace;background-color:#f4f4f4;padding:2px 4px;border-radius:3px;\">$1</span>", 
            RegexOptions.Singleline);

        // === Links ===
        // Convert <a> to styled links with explicit color and underline
        html = Regex.Replace(html, @"<a\s+href=""([^""]+)""[^>]*>(.*?)</a>", match =>
        {
            var href = match.Groups[1].Value;
            var text = match.Groups[2].Value;
            return $"<a href=\"{href}\" style=\"color:#0563C1;text-decoration:underline;\">{text}</a>";
        }, RegexOptions.Singleline);

        // === Headings ===
        html = Regex.Replace(html, @"<h1>(.*?)</h1>", 
            "<div style=\"font-size:24pt;font-weight:bold;margin:16px 0 8px 0;\">$1</div>", RegexOptions.Singleline);
        html = Regex.Replace(html, @"<h2>(.*?)</h2>", 
            "<div style=\"font-size:18pt;font-weight:bold;margin:14px 0 6px 0;\">$1</div>", RegexOptions.Singleline);
        html = Regex.Replace(html, @"<h3>(.*?)</h3>", 
            "<div style=\"font-size:14pt;font-weight:bold;margin:12px 0 4px 0;\">$1</div>", RegexOptions.Singleline);
        html = Regex.Replace(html, @"<h4>(.*?)</h4>", 
            "<div style=\"font-size:12pt;font-weight:bold;margin:10px 0 4px 0;\">$1</div>", RegexOptions.Singleline);
        html = Regex.Replace(html, @"<h5>(.*?)</h5>", 
            "<div style=\"font-size:11pt;font-weight:bold;margin:8px 0 4px 0;\">$1</div>", RegexOptions.Singleline);
        html = Regex.Replace(html, @"<h6>(.*?)</h6>", 
            "<div style=\"font-size:10pt;font-weight:bold;margin:8px 0 4px 0;\">$1</div>", RegexOptions.Singleline);

        // === Paragraphs ===
        html = Regex.Replace(html, @"<p>(.*?)</p>", 
            "<div style=\"margin:0 0 10px 0;\">$1</div>", RegexOptions.Singleline);

        // === Lists ===
        // Unordered lists
        html = Regex.Replace(html, @"<ul>(.*?)</ul>", 
            "<div style=\"margin:10px 0 10px 20px;\">$1</div>", RegexOptions.Singleline);
        // Ordered lists  
        html = Regex.Replace(html, @"<ol>(.*?)</ol>", 
            "<div style=\"margin:10px 0 10px 20px;\">$1</div>", RegexOptions.Singleline);
        // List items - add bullet/number via content
        html = Regex.Replace(html, @"<li>(.*?)</li>", 
            "<div style=\"margin:4px 0;\">• $1</div>", RegexOptions.Singleline);

        // === Blockquotes ===
        html = Regex.Replace(html, @"<blockquote>(.*?)</blockquote>", 
            "<div style=\"border-left:4px solid #ccc;margin:10px 0;padding:8px 12px;color:#666;background-color:#f9f9f9;\">$1</div>", 
            RegexOptions.Singleline);

        // === Horizontal Rule ===
        html = Regex.Replace(html, @"<hr\s*/?>", 
            "<div style=\"border:0;border-top:1px solid #ccc;margin:20px 0;\"></div>");

        // === Code Blocks ===
        html = Regex.Replace(html, @"<pre><code[^>]*>(.*?)</code></pre>", 
            "<div style=\"font-family:Consolas,Monaco,monospace;background-color:#f4f4f4;padding:12px;border-radius:4px;margin:10px 0;white-space:pre-wrap;\">$1</div>", 
            RegexOptions.Singleline);
        html = Regex.Replace(html, @"<pre>(.*?)</pre>", 
            "<div style=\"font-family:Consolas,Monaco,monospace;background-color:#f4f4f4;padding:12px;border-radius:4px;margin:10px 0;white-space:pre-wrap;\">$1</div>", 
            RegexOptions.Singleline);

        // === Line breaks ===
        // Ensure <br> tags are self-closing for XHTML compatibility
        html = Regex.Replace(html, @"<br\s*>", "<br />");

        return html;
    }

    /// <summary>
    /// Wraps HTML content with Outlook-compatible base styling.
    /// </summary>
    private static string WrapForOutlook(string htmlContent)
    {
        return $"<div style=\"font-family:'Segoe UI',Calibri,Arial,sans-serif;font-size:11pt;color:#333333;\">{htmlContent}</div>";
    }

    /// <summary>
    /// Checks if the text contains any markdown formatting.
    /// </summary>
    public static bool ContainsMarkdown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("**") ||  // Bold
               text.Contains("__") ||  // Bold alt
               text.Contains('*') ||   // Italic or list
               text.Contains('_') ||   // Italic alt
               text.Contains("~~") ||  // Strikethrough
               text.Contains('#') ||   // Headers
               text.Contains('[') ||   // Links
               text.Contains('`') ||   // Code
               text.Contains('>') ||   // Blockquote
               text.Contains('-') ||   // List or horizontal rule
               text.Contains("1.");    // Numbered list
    }
}
