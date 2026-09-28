using System;
using System.Text.RegularExpressions;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;

namespace WinAI.Common
{
    /// <summary>
    /// Attached property that parses markdown text (bold, italic, inline code, code blocks, bullet points)
    /// into native UWP RichTextBlock inlines and blocks.
    /// </summary>
    public static class MarkdownRenderer
    {
        public static readonly DependencyProperty MarkdownProperty =
            DependencyProperty.RegisterAttached(
                "Markdown",
                typeof(string),
                typeof(MarkdownRenderer),
                new PropertyMetadata(null, OnMarkdownChanged));

        public static string GetMarkdown(DependencyObject obj)
        {
            return (string)obj.GetValue(MarkdownProperty);
        }

        public static void SetMarkdown(DependencyObject obj, string value)
        {
            obj.SetValue(MarkdownProperty, value);
        }

        private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RichTextBlock richText)
            {
                string markdown = e.NewValue as string;
                RenderMarkdown(richText, markdown);
            }
        }

        public static void RenderMarkdown(RichTextBlock richText, string markdown)
        {
            richText.Blocks.Clear();

            if (string.IsNullOrEmpty(markdown))
            {
                return;
            }

            // Normalize newlines
            string normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');

            // Split on code blocks: ```lang ... ```
            string[] parts = Regex.Split(normalized, @"(```[\s\S]*?```)");

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;

                if (part.StartsWith("```") && part.EndsWith("```") && part.Length >= 6)
                {
                    // Code Block
                    string codeContent = part.Substring(3, part.Length - 6);
                    string lang = "";
                    int firstLineBreak = codeContent.IndexOf('\n');
                    if (firstLineBreak > 0)
                    {
                        lang = codeContent.Substring(0, firstLineBreak).Trim();
                        codeContent = codeContent.Substring(firstLineBreak + 1);
                    }

                    var codeBlock = CreateCodeBlockElement(codeContent.TrimEnd(), lang);
                    var container = new InlineUIContainer { Child = codeBlock };
                    var paragraph = new Paragraph();
                    paragraph.Margin = new Thickness(0, 4, 0, 4);
                    paragraph.Inlines.Add(container);
                    richText.Blocks.Add(paragraph);
                }
                else
                {
                    // Regular paragraphs
                    string[] lines = part.Split('\n');
                    var currentParagraph = new Paragraph();
                    currentParagraph.Margin = new Thickness(0, 2, 0, 2);

                    foreach (var rawLine in lines)
                    {
                        string line = rawLine;

                        if (string.IsNullOrWhiteSpace(line))
                        {
                            if (currentParagraph.Inlines.Count > 0)
                            {
                                richText.Blocks.Add(currentParagraph);
                                currentParagraph = new Paragraph();
                                currentParagraph.Margin = new Thickness(0, 2, 0, 2);
                            }
                            continue;
                        }

                        // Check Header (# Heading)
                        if (line.StartsWith("### "))
                        {
                            var headerRun = new Run
                            {
                                Text = line.Substring(4).Trim(),
                                FontWeight = FontWeights.SemiBold,
                                FontSize = richText.FontSize + 1
                            };
                            currentParagraph.Inlines.Add(headerRun);
                            currentParagraph.Inlines.Add(new LineBreak());
                            continue;
                        }
                        else if (line.StartsWith("## "))
                        {
                            var headerRun = new Run
                            {
                                Text = line.Substring(3).Trim(),
                                FontWeight = FontWeights.Bold,
                                FontSize = richText.FontSize + 2
                            };
                            currentParagraph.Inlines.Add(headerRun);
                            currentParagraph.Inlines.Add(new LineBreak());
                            continue;
                        }
                        else if (line.StartsWith("# "))
                        {
                            var headerRun = new Run
                            {
                                Text = line.Substring(2).Trim(),
                                FontWeight = FontWeights.Bold,
                                FontSize = richText.FontSize + 3
                            };
                            currentParagraph.Inlines.Add(headerRun);
                            currentParagraph.Inlines.Add(new LineBreak());
                            continue;
                        }

                        // Check Bullet point (* or -)
                        if (line.StartsWith("* ") || line.StartsWith("- "))
                        {
                            currentParagraph.Inlines.Add(new Run { Text = "• ", FontWeight = FontWeights.Bold });
                            line = line.Substring(2);
                        }

                        // Parse inlines: bold (**text**), italic (*text*), code (`text`)
                        ParseInlines(currentParagraph, line);
                        currentParagraph.Inlines.Add(new LineBreak());
                    }

                    if (currentParagraph.Inlines.Count > 0)
                    {
                        richText.Blocks.Add(currentParagraph);
                    }
                }
            }
        }

        private static void ParseInlines(Paragraph paragraph, string text)
        {
            // Regex to find **bold**, *italic*, `code`
            var pattern = @"(\*\*[^*]+?\*\*|\*[^*]+?\*|`[^`]+?`)";
            string[] tokens = Regex.Split(text, pattern);

            foreach (var token in tokens)
            {
                if (string.IsNullOrEmpty(token)) continue;

                if (token.StartsWith("**") && token.EndsWith("**") && token.Length > 4)
                {
                    // Bold
                    string boldText = token.Substring(2, token.Length - 4);
                    var bold = new Bold();
                    bold.Inlines.Add(new Run { Text = boldText });
                    paragraph.Inlines.Add(bold);
                }
                else if (token.StartsWith("`") && token.EndsWith("`") && token.Length > 2)
                {
                    // Inline code
                    string codeText = token.Substring(1, token.Length - 2);
                    var codeRun = new Run
                    {
                        Text = codeText,
                        FontFamily = new FontFamily("Consolas")
                    };
                    paragraph.Inlines.Add(codeRun);
                }
                else if (token.StartsWith("*") && token.EndsWith("*") && token.Length > 2)
                {
                    // Italic
                    string italicText = token.Substring(1, token.Length - 2);
                    var italic = new Italic();
                    italic.Inlines.Add(new Run { Text = italicText });
                    paragraph.Inlines.Add(italic);
                }
                else
                {
                    // Plain text
                    paragraph.Inlines.Add(new Run { Text = token });
                }
            }
        }

        private static FrameworkElement CreateCodeBlockElement(string code, string lang)
        {
            var border = new Border
            {
                Background = Application.Current.Resources["SystemControlBackgroundAltHighBrush"] as Brush,
                BorderBrush = Application.Current.Resources["SystemControlForegroundBaseLowBrush"] as Brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var stack = new StackPanel();

            if (!string.IsNullOrWhiteSpace(lang))
            {
                var langText = new TextBlock
                {
                    Text = lang.ToUpperInvariant(),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Application.Current.Resources["SystemControlForegroundAccentBrush"] as Brush,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                stack.Children.Add(langText);
            }

            var textBlock = new TextBlock
            {
                Text = code,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Foreground = Application.Current.Resources["SystemControlForegroundBaseHighBrush"] as Brush
            };

            stack.Children.Add(textBlock);
            border.Child = stack;
            return border;
        }
    }
}
