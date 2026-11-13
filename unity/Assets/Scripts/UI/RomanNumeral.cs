namespace InfinitySpace
{
    public static class RomanNumeral
    {
        private static readonly int[] Values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        private static readonly string[] Symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

        public static string ToRoman(int number)
        {
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < Values.Length && number > 0; i++)
            {
                while (number >= Values[i])
                {
                    number -= Values[i];
                    result.Append(Symbols[i]);
                }
            }
            return result.ToString();
        }
    }
}
