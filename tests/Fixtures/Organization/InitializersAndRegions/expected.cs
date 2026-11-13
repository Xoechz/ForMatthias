class Example
{
    #region Fields
    private int z = Log("first");
    #endregion

    #region Properties
    public string Name { get; } = LogText();
    #endregion

    #region Fields
    private int a = Log("last");
    #endregion
}
