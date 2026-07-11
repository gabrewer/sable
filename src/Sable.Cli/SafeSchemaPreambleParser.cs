// Copyright 2024 Bloomberg Finance L.P.
// Distributed under the terms of the MIT license.

using System.Text;

namespace Sable.Cli;

internal static class SafeSchemaPreambleParser
{
    public static bool TrySplit(
        string script,
        string databaseSchemaName,
        out string schemaPreambles,
        out string remainingScript
    )
    {
        var statementStart = SkipTrivia(script, 0);
        if (!IsKeywordAt(script, statementStart, "DO"))
        {
            schemaPreambles = string.Empty;
            remainingScript = script;
            return false;
        }

        var preambleEnd = 0;
        while (IsKeywordAt(script, statementStart, "DO"))
        {
            preambleEnd = ParseSafeDoStatement(script, statementStart, databaseSchemaName);
            var nextStatementStart = SkipTrivia(script, preambleEnd);
            if (!IsKeywordAt(script, nextStatementStart, "DO"))
            {
                break;
            }

            statementStart = nextStatementStart;
        }

        schemaPreambles = script[..preambleEnd];
        remainingScript = script[preambleEnd..];
        return true;
    }

    private static int ParseSafeDoStatement(
        string script,
        int statementStart,
        string databaseSchemaName
    )
    {
        var position = statementStart + 2;
        position = SkipTrivia(script, position);

        if (IsKeywordAt(script, position, "LANGUAGE"))
        {
            position += "LANGUAGE".Length;
            position = SkipTrivia(script, position);
            if (!IsKeywordAt(script, position, "plpgsql"))
            {
                throw new FormatException("Unsupported DO language.");
            }

            position += "plpgsql".Length;
            position = SkipTrivia(script, position);
        }

        if (!TryReadDollarDelimiter(script, position, out var delimiter, out var bodyStart))
        {
            throw new FormatException("Missing DO dollar delimiter.");
        }

        var bodyEnd = script.IndexOf(delimiter, bodyStart, StringComparison.Ordinal);
        if (bodyEnd < 0)
        {
            throw new FormatException("Unterminated DO body.");
        }

        var body = script[bodyStart..bodyEnd];
        if (!IsSafeSchemaBody(body, databaseSchemaName))
        {
            throw new FormatException("Unsupported DO body.");
        }

        position = bodyEnd + delimiter.Length;
        position = SkipTrivia(script, position);
        if (position >= script.Length || script[position] != ';')
        {
            throw new FormatException("Missing DO statement terminator.");
        }

        return position + 1;
    }

    private static bool IsSafeSchemaBody(string body, string databaseSchemaName)
    {
        var reader = new SqlReader(body);
        if (!reader.ReadKeyword("BEGIN") || !reader.ReadKeyword("BEGIN"))
        {
            return false;
        }

        if (!reader.ReadKeyword("EXECUTE") || !reader.ReadStringLiteral(out var createSchemaSql))
        {
            return false;
        }

        if (!reader.ReadRequiredSemicolon() || !IsSafeCreateSchema(createSchemaSql, databaseSchemaName))
        {
            return false;
        }

        if (!reader.ReadKeyword("EXCEPTION"))
        {
            return false;
        }

        var handlers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < 2; i++)
        {
            if (
                !reader.ReadKeyword("WHEN")
                || !reader.ReadIdentifier(out var handler, out _)
                || !handlers.Add(handler)
                || !IsApprovedHandler(handler)
                || !reader.ReadKeyword("THEN")
                || !reader.ReadKeyword("NULL")
                || !reader.ReadRequiredSemicolon()
            )
            {
                return false;
            }
        }

        if (
            handlers.Count != 2
            || !reader.ReadKeyword("END")
            || !reader.ReadRequiredSemicolon()
            || !reader.ReadKeyword("END")
        )
        {
            return false;
        }

        reader.ReadOptionalSemicolon();
        return reader.IsAtEnd();
    }

    private static bool IsApprovedHandler(string handler)
    {
        return handler.Equals("duplicate_schema", StringComparison.OrdinalIgnoreCase)
            || handler.Equals("unique_violation", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeCreateSchema(string sql, string databaseSchemaName)
    {
        var reader = new SqlReader(sql);
        if (
            !reader.ReadKeyword("CREATE")
            || !reader.ReadKeyword("SCHEMA")
            || !reader.ReadKeyword("IF")
            || !reader.ReadKeyword("NOT")
            || !reader.ReadKeyword("EXISTS")
            || !reader.ReadIdentifier(out var schemaName, out var quoted)
        )
        {
            return false;
        }

        reader.ReadOptionalSemicolon();
        return reader.IsAtEnd() && SchemaNamesMatch(schemaName, quoted, databaseSchemaName);
    }

    private static bool SchemaNamesMatch(
        string parsedSchemaName,
        bool parsedWasQuoted,
        string databaseSchemaName
    )
    {
        var configuredWasQuoted =
            databaseSchemaName.Length >= 2
            && databaseSchemaName[0] == '"'
            && databaseSchemaName[^1] == '"';
        var configuredSchemaName = configuredWasQuoted
            ? databaseSchemaName[1..^1].Replace("\"\"", "\"")
            : databaseSchemaName;

        return string.Equals(
            parsedSchemaName,
            configuredSchemaName,
            parsedWasQuoted || configuredWasQuoted
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool TryReadDollarDelimiter(
        string script,
        int position,
        out string delimiter,
        out int bodyStart
    )
    {
        delimiter = string.Empty;
        bodyStart = position;
        if (position >= script.Length || script[position] != '$')
        {
            return false;
        }

        var closingDollar = script.IndexOf('$', position + 1);
        if (closingDollar < 0)
        {
            return false;
        }

        var tag = script[(position + 1)..closingDollar];
        if (
            tag.Length > 0
            && (!IsIdentifierStart(tag[0]) || tag.Skip(1).Any(character => !IsIdentifierPart(character)))
        )
        {
            return false;
        }

        delimiter = script[position..(closingDollar + 1)];
        bodyStart = closingDollar + 1;
        return true;
    }

    private static int SkipTrivia(string text, int position)
    {
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
                continue;
            }

            if (position + 1 < text.Length && text[position] == '-' && text[position + 1] == '-')
            {
                position += 2;
                while (position < text.Length && text[position] is not ('\r' or '\n'))
                {
                    position++;
                }
                continue;
            }

            if (position + 1 < text.Length && text[position] == '/' && text[position + 1] == '*')
            {
                position = SkipBlockComment(text, position);
                continue;
            }

            break;
        }

        return position;
    }

    private static int SkipBlockComment(string text, int position)
    {
        var depth = 1;
        position += 2;
        while (position < text.Length && depth > 0)
        {
            if (position + 1 < text.Length && text[position] == '/' && text[position + 1] == '*')
            {
                depth++;
                position += 2;
            }
            else if (
                position + 1 < text.Length && text[position] == '*' && text[position + 1] == '/'
            )
            {
                depth--;
                position += 2;
            }
            else
            {
                position++;
            }
        }

        return position;
    }

    private static bool IsKeywordAt(string text, int position, string keyword)
    {
        if (position < 0 || position + keyword.Length > text.Length)
        {
            return false;
        }

        if (!text.AsSpan(position, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var beforeIsIdentifier = position > 0 && IsIdentifierPart(text[position - 1]);
        var after = position + keyword.Length;
        var afterIsIdentifier = after < text.Length && IsIdentifierPart(text[after]);
        return !beforeIsIdentifier && !afterIsIdentifier;
    }

    private static bool IsIdentifierStart(char character)
    {
        return character == '_' || char.IsLetter(character);
    }

    private static bool IsIdentifierPart(char character)
    {
        return character is '_' or '$' || char.IsLetterOrDigit(character);
    }

    private sealed class SqlReader
    {
        private readonly string _text;
        private int _position;

        public SqlReader(string text)
        {
            _text = text;
        }

        public bool ReadKeyword(string keyword)
        {
            var start = SkipTrivia(_text, _position);
            if (!IsKeywordAt(_text, start, keyword))
            {
                return false;
            }

            _position = start + keyword.Length;
            return true;
        }

        public bool ReadIdentifier(out string identifier, out bool quoted)
        {
            _position = SkipTrivia(_text, _position);
            identifier = string.Empty;
            quoted = false;
            if (_position >= _text.Length)
            {
                return false;
            }

            if (_text[_position] == '"')
            {
                quoted = true;
                _position++;
                var builder = new StringBuilder();
                while (_position < _text.Length)
                {
                    if (_text[_position] != '"')
                    {
                        builder.Append(_text[_position++]);
                        continue;
                    }

                    if (_position + 1 < _text.Length && _text[_position + 1] == '"')
                    {
                        builder.Append('"');
                        _position += 2;
                        continue;
                    }

                    _position++;
                    identifier = builder.ToString();
                    return true;
                }

                return false;
            }

            if (!IsIdentifierStart(_text[_position]))
            {
                return false;
            }

            var start = _position++;
            while (_position < _text.Length && IsIdentifierPart(_text[_position]))
            {
                _position++;
            }

            identifier = _text[start.._position];
            return true;
        }

        public bool ReadStringLiteral(out string value)
        {
            _position = SkipTrivia(_text, _position);
            value = string.Empty;
            if (_position >= _text.Length || _text[_position] != '\'')
            {
                return false;
            }

            _position++;
            var builder = new StringBuilder();
            while (_position < _text.Length)
            {
                if (_text[_position] != '\'')
                {
                    builder.Append(_text[_position++]);
                    continue;
                }

                if (_position + 1 < _text.Length && _text[_position + 1] == '\'')
                {
                    builder.Append('\'');
                    _position += 2;
                    continue;
                }

                _position++;
                value = builder.ToString();
                return true;
            }

            return false;
        }

        public bool ReadRequiredSemicolon()
        {
            _position = SkipTrivia(_text, _position);
            if (_position >= _text.Length || _text[_position] != ';')
            {
                return false;
            }

            _position++;
            return true;
        }

        public void ReadOptionalSemicolon()
        {
            _position = SkipTrivia(_text, _position);
            if (_position < _text.Length && _text[_position] == ';')
            {
                _position++;
            }
        }

        public bool IsAtEnd()
        {
            return SkipTrivia(_text, _position) == _text.Length;
        }
    }
}
