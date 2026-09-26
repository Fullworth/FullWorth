namespace FullWorth.API.Services.Intelligence;

/// <summary>
/// Correctness-first CPU reference implementation of the FullWorth decoder.
/// It exists to prove model semantics before a GPU backend is selected.
/// It is not the production inference performance path.
/// </summary>
public sealed class FullWorthReferenceDecoder
{
    private const double RmsEpsilon = 1e-5d;

    private readonly FullWorthLanguageModelArchitecture _architecture;
    private readonly FullWorthInitializedModelState _state;

    public FullWorthReferenceDecoder(
        FullWorthLanguageModelArchitecture architecture,
        FullWorthInitializedModelState state)
    {
        _architecture =
            architecture ??
            throw new ArgumentNullException(nameof(architecture));

        _state =
            state ??
            throw new ArgumentNullException(nameof(state));

        if (!string.Equals(
                architecture.CompatibilityId,
                state.Layout.Architecture.CompatibilityId,
                StringComparison.Ordinal))
            throw new ArgumentException(
                "The initialized model state does not match the decoder architecture.",
                nameof(state));
    }

    public float[] ForwardNextTokenLogits(
        ReadOnlySpan<int> tokens)
    {
        float[][] allLogits =
            ForwardAllTokenLogits(tokens);

        return allLogits[^1];
    }

    /// <summary>
    /// Returns one vocabulary-logit vector for every supplied position.
    /// Causal attention guarantees that a position cannot observe later tokens.
    /// </summary>
    public float[][] ForwardAllTokenLogits(
        ReadOnlySpan<int> tokens)
    {
        ValidateTokens(tokens);

        int sequenceLength =
            tokens.Length;

        int hiddenSize =
            _architecture.HiddenSize;

        var hidden =
            new float[
                checked(sequenceLength * hiddenSize)];

        LoadEmbeddings(
            tokens,
            hidden);

        for (int layer = 0;
             layer < _architecture.LayerCount;
             layer++)
        {
            ApplyAttentionBlock(
                hidden,
                sequenceLength,
                layer);

            ApplyFeedForwardBlock(
                hidden,
                sequenceLength,
                layer);
        }

        var finalHidden =
            new float[hidden.Length];

        ApplyRmsNorm(
            hidden,
            _state
                .GetRequiredTensor("finalNorm.weight")
                .Span,
            finalHidden,
            sequenceLength,
            hiddenSize);

        return ProjectLogits(
            finalHidden,
            sequenceLength);
    }

    private void ValidateTokens(
        ReadOnlySpan<int> tokens)
    {
        if (tokens.IsEmpty)
            throw new ArgumentException(
                "At least one token is required.",
                nameof(tokens));

        if (tokens.Length > _architecture.ContextLength)
            throw new ArgumentException(
                "The token sequence exceeds the model context length.",
                nameof(tokens));

        foreach (int token in tokens)
        {
            if (token <= FullWorthByteTokenizer.PaddingToken ||
                token >= _architecture.VocabularySize)
                throw new ArgumentOutOfRangeException(
                    nameof(tokens),
                    "Inference tokens must be unpadded vocabulary tokens.");
        }
    }

    private void LoadEmbeddings(
        ReadOnlySpan<int> tokens,
        Span<float> hidden)
    {
        int hiddenSize =
            _architecture.HiddenSize;

        ReadOnlySpan<float> embedding =
            _state
                .GetRequiredTensor(
                    FullWorthModelTensorLayout.TokenEmbeddingTensorName)
                .Span;

        for (int position = 0;
             position < tokens.Length;
             position++)
        {
            int sourceOffset =
                checked(tokens[position] * hiddenSize);

            embedding
                .Slice(
                    sourceOffset,
                    hiddenSize)
                .CopyTo(
                    hidden.Slice(
                        position * hiddenSize,
                        hiddenSize));
        }
    }

    private void ApplyAttentionBlock(
        Span<float> hidden,
        int sequenceLength,
        int layer)
    {
        int hiddenSize =
            _architecture.HiddenSize;

        int keyValueWidth =
            checked(
                _architecture.HeadSize *
                _architecture.KeyValueHeadCount);

        var normalized =
            new float[hidden.Length];

        ApplyRmsNorm(
            hidden,
            _state
                .GetRequiredTensor(
                    $"layers.{layer}.attentionNorm.weight")
                .Span,
            normalized,
            sequenceLength,
            hiddenSize);

        var query =
            new float[
                checked(sequenceLength * hiddenSize)];

        var key =
            new float[
                checked(sequenceLength * keyValueWidth)];

        var value =
            new float[
                checked(sequenceLength * keyValueWidth)];

        ProjectSequence(
            normalized,
            sequenceLength,
            hiddenSize,
            _state
                .GetRequiredTensor(
                    $"layers.{layer}.attention.query.weight")
                .Span,
            hiddenSize,
            query);

        ProjectSequence(
            normalized,
            sequenceLength,
            hiddenSize,
            _state
                .GetRequiredTensor(
                    $"layers.{layer}.attention.key.weight")
                .Span,
            keyValueWidth,
            key);

        ProjectSequence(
            normalized,
            sequenceLength,
            hiddenSize,
            _state
                .GetRequiredTensor(
                    $"layers.{layer}.attention.value.weight")
                .Span,
            keyValueWidth,
            value);

        ApplyRotaryPositionEncoding(
            query,
            sequenceLength,
            _architecture.AttentionHeadCount);

        ApplyRotaryPositionEncoding(
            key,
            sequenceLength,
            _architecture.KeyValueHeadCount);

        var attended =
            new float[
                checked(sequenceLength * hiddenSize)];

        ApplyGroupedQueryCausalAttention(
            query,
            key,
            value,
            attended,
            sequenceLength);

        var projected =
            new float[hidden.Length];

        ProjectSequence(
            attended,
            sequenceLength,
            hiddenSize,
            _state
                .GetRequiredTensor(
                    $"layers.{layer}.attention.output.weight")
                .Span,
            hiddenSize,
            projected);

        AddResidual(
            hidden,
            projected);
    }

    private void ApplyFeedForwardBlock(
        Span<float> hidden,
        int sequenceLength,
        int layer)
    {
        int hiddenSize =
            _architecture.HiddenSize;

        int feedForwardSize =
            _architecture.FeedForwardSize;

        var normalized =
            new float[hidden.Length];

        ApplyRmsNorm(
            hidden,
            _state
                .GetRequiredTensor(
                    $"layers.{layer}.feedForwardNorm.weight")
                .Span,
            normalized,
            sequenceLength,
            hiddenSize);

        var gate =
            new float[
                checked(sequenceLength * feedForwardSize)];

        var up =
            new float[gate.Length];

        ProjectSequence(
            normalized,
            sequenceLength,
            hiddenSize,
            _state
                .GetRequiredTensor(
                    $"layers.{layer}.feedForward.gate.weight")
                .Span,
            feedForwardSize,
            gate);

        ProjectSequence(
            normalized,
            sequenceLength,
            hiddenSize,
            _state
                .GetRequiredTensor(
                    $"layers.{layer}.feedForward.up.weight")
                .Span,
            feedForwardSize,
            up);

        for (int index = 0;
             index < gate.Length;
             index++)
        {
            gate[index] =
                Silu(gate[index]) *
                up[index];
        }

        var down =
            new float[hidden.Length];

        ProjectSequence(
            gate,
            sequenceLength,
            feedForwardSize,
            _state
                .GetRequiredTensor(
                    $"layers.{layer}.feedForward.down.weight")
                .Span,
            hiddenSize,
            down);

        AddResidual(
            hidden,
            down);
    }

    private void ApplyRotaryPositionEncoding(
        Span<float> projected,
        int sequenceLength,
        int headCount)
    {
        int headSize =
            _architecture.HeadSize;

        int width =
            checked(headCount * headSize);

        for (int position = 0;
             position < sequenceLength;
             position++)
        {
            int positionOffset =
                checked(position * width);

            for (int head = 0;
                 head < headCount;
                 head++)
            {
                int headOffset =
                    checked(
                        positionOffset +
                        (head * headSize));

                for (int pairOffset = 0;
                     pairOffset < headSize;
                     pairOffset += 2)
                {
                    double inverseFrequency =
                        Math.Pow(
                            _architecture.RotaryTheta,
                            -(double)pairOffset /
                            headSize);

                    double angle =
                        position *
                        inverseFrequency;

                    double cosine =
                        Math.Cos(angle);

                    double sine =
                        Math.Sin(angle);

                    int firstIndex =
                        headOffset +
                        pairOffset;

                    int secondIndex =
                        firstIndex + 1;

                    float first =
                        projected[firstIndex];

                    float second =
                        projected[secondIndex];

                    projected[firstIndex] =
                        (float)(
                            (first * cosine) -
                            (second * sine));

                    projected[secondIndex] =
                        (float)(
                            (first * sine) +
                            (second * cosine));
                }
            }
        }
    }

    private void ApplyGroupedQueryCausalAttention(
        ReadOnlySpan<float> query,
        ReadOnlySpan<float> key,
        ReadOnlySpan<float> value,
        Span<float> destination,
        int sequenceLength)
    {
        int headSize =
            _architecture.HeadSize;

        int queryHeadCount =
            _architecture.AttentionHeadCount;

        int keyValueHeadCount =
            _architecture.KeyValueHeadCount;

        int queryWidth =
            _architecture.HiddenSize;

        int keyValueWidth =
            checked(
                headSize *
                keyValueHeadCount);

        int headsPerKeyValueHead =
            queryHeadCount /
            keyValueHeadCount;

        double scale =
            1d /
            Math.Sqrt(headSize);

        for (int position = 0;
             position < sequenceLength;
             position++)
        {
            for (int queryHead = 0;
                 queryHead < queryHeadCount;
                 queryHead++)
            {
                int keyValueHead =
                    queryHead /
                    headsPerKeyValueHead;

                int queryOffset =
                    checked(
                        (position * queryWidth) +
                        (queryHead * headSize));

                double maximumScore =
                    double.NegativeInfinity;

                for (int source = 0;
                     source <= position;
                     source++)
                {
                    int keyOffset =
                        checked(
                            (source * keyValueWidth) +
                            (keyValueHead * headSize));

                    double score =
                        Dot(
                            query.Slice(
                                queryOffset,
                                headSize),
                            key.Slice(
                                keyOffset,
                                headSize)) *
                        scale;

                    if (score > maximumScore)
                        maximumScore = score;
                }

                double denominator = 0d;

                for (int source = 0;
                     source <= position;
                     source++)
                {
                    int keyOffset =
                        checked(
                            (source * keyValueWidth) +
                            (keyValueHead * headSize));

                    double score =
                        Dot(
                            query.Slice(
                                queryOffset,
                                headSize),
                            key.Slice(
                                keyOffset,
                                headSize)) *
                        scale;

                    denominator +=
                        Math.Exp(
                            score -
                            maximumScore);
                }

                int destinationOffset =
                    checked(
                        (position * queryWidth) +
                        (queryHead * headSize));

                for (int dimension = 0;
                     dimension < headSize;
                     dimension++)
                {
                    double weightedValue = 0d;

                    for (int source = 0;
                         source <= position;
                         source++)
                    {
                        int keyOffset =
                            checked(
                                (source * keyValueWidth) +
                                (keyValueHead * headSize));

                        double score =
                            Dot(
                                query.Slice(
                                    queryOffset,
                                    headSize),
                                key.Slice(
                                    keyOffset,
                                    headSize)) *
                            scale;

                        double probability =
                            Math.Exp(
                                score -
                                maximumScore) /
                            denominator;

                        int valueIndex =
                            checked(
                                (source * keyValueWidth) +
                                (keyValueHead * headSize) +
                                dimension);

                        weightedValue +=
                            probability *
                            value[valueIndex];
                    }

                    destination[
                        destinationOffset +
                        dimension] =
                        (float)weightedValue;
                }
            }
        }
    }

    private float[][] ProjectLogits(
        ReadOnlySpan<float> hidden,
        int sequenceLength)
    {
        int hiddenSize =
            _architecture.HiddenSize;

        int vocabularySize =
            _architecture.VocabularySize;

        ReadOnlySpan<float> embedding =
            _state
                .GetRequiredTensor(
                    FullWorthModelTensorLayout.TokenEmbeddingTensorName)
                .Span;

        var logits =
            new float[sequenceLength][];

        for (int position = 0;
             position < sequenceLength;
             position++)
        {
            var positionLogits =
                new float[vocabularySize];

            ReadOnlySpan<float> vector =
                hidden.Slice(
                    position * hiddenSize,
                    hiddenSize);

            for (int token = 0;
                 token < vocabularySize;
                 token++)
            {
                positionLogits[token] =
                    (float)Dot(
                        embedding.Slice(
                            token * hiddenSize,
                            hiddenSize),
                        vector);
            }

            logits[position] =
                positionLogits;
        }

        return logits;
    }

    private static void ApplyRmsNorm(
        ReadOnlySpan<float> source,
        ReadOnlySpan<float> scale,
        Span<float> destination,
        int sequenceLength,
        int hiddenSize)
    {
        if (scale.Length != hiddenSize)
            throw new InvalidOperationException(
                "RMSNorm scale shape does not match hidden size.");

        for (int position = 0;
             position < sequenceLength;
             position++)
        {
            int offset =
                checked(position * hiddenSize);

            double squareSum = 0d;

            for (int dimension = 0;
                 dimension < hiddenSize;
                 dimension++)
            {
                double value =
                    source[offset + dimension];

                squareSum +=
                    value *
                    value;
            }

            double inverseRootMeanSquare =
                1d /
                Math.Sqrt(
                    (squareSum / hiddenSize) +
                    RmsEpsilon);

            for (int dimension = 0;
                 dimension < hiddenSize;
                 dimension++)
            {
                destination[offset + dimension] =
                    (float)(
                        source[offset + dimension] *
                        inverseRootMeanSquare *
                        scale[dimension]);
            }
        }
    }

    private static void ProjectSequence(
        ReadOnlySpan<float> source,
        int sequenceLength,
        int inputWidth,
        ReadOnlySpan<float> matrix,
        int outputWidth,
        Span<float> destination)
    {
        int expectedMatrixLength =
            checked(outputWidth * inputWidth);

        int expectedSourceLength =
            checked(sequenceLength * inputWidth);

        int expectedDestinationLength =
            checked(sequenceLength * outputWidth);

        if (matrix.Length != expectedMatrixLength ||
            source.Length != expectedSourceLength ||
            destination.Length != expectedDestinationLength)
            throw new InvalidOperationException(
                "Projection tensor shape does not match the requested operation.");

        for (int position = 0;
             position < sequenceLength;
             position++)
        {
            ReadOnlySpan<float> vector =
                source.Slice(
                    position * inputWidth,
                    inputWidth);

            int destinationOffset =
                checked(position * outputWidth);

            for (int row = 0;
                 row < outputWidth;
                 row++)
            {
                destination[
                    destinationOffset +
                    row] =
                    (float)Dot(
                        matrix.Slice(
                            row * inputWidth,
                            inputWidth),
                        vector);
            }
        }
    }

    private static double Dot(
        ReadOnlySpan<float> left,
        ReadOnlySpan<float> right)
    {
        if (left.Length != right.Length)
            throw new ArgumentException(
                "Dot-product vectors must have identical lengths.");

        double total = 0d;

        for (int index = 0;
             index < left.Length;
             index++)
        {
            total +=
                left[index] *
                right[index];
        }

        return total;
    }

    private static float Silu(
        float value)
    {
        double x =
            value;

        double sigmoid;

        if (x >= 0d)
        {
            sigmoid =
                1d /
                (1d + Math.Exp(-x));
        }
        else
        {
            double exponential =
                Math.Exp(x);

            sigmoid =
                exponential /
                (1d + exponential);
        }

        return
            (float)(
                x *
                sigmoid);
    }

    private static void AddResidual(
        Span<float> destination,
        ReadOnlySpan<float> residual)
    {
        if (destination.Length != residual.Length)
            throw new InvalidOperationException(
                "Residual tensor shape does not match hidden state.");

        for (int index = 0;
             index < destination.Length;
             index++)
        {
            destination[index] +=
                residual[index];
        }
    }
}
