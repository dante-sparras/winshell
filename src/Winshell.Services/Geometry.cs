namespace Winshell;

public readonly record struct QPoint(double X, double Y)
{
    public override string ToString() => FormattableString.Invariant($"QPointF({X}, {Y})");
}

public readonly record struct QSize(double Width, double Height)
{
    public override string ToString() => FormattableString.Invariant($"QSizeF({Width}, {Height})");
}

public readonly record struct QRect(double X, double Y, double Width, double Height)
{
    public double Left => X;

    public double Top => Y;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Contains(double x, double y) => x >= X && y >= Y && x < X + Width && y < Y + Height;

    public bool IntersectsWith(QRect other) =>
        other.X < Right && other.Right > X && other.Y < Bottom && other.Bottom > Y;

    public override string ToString() => FormattableString.Invariant($"QRectF({X}, {Y}, {Width}, {Height})");
}
