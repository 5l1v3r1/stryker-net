﻿namespace ExampleProject.Math
{
    public class DummyMath
    {
        public int Add(int first, int second)
        {
            return checked(first + second);
        }
    }
}
