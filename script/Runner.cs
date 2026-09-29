namespace RaceSimulation.Model
{
  /// <summary>
  /// Represents a Race Participant
  /// </summary>
  class Runner
  {
    private string _id;
    private int _speed;
    public float[] _sectionSpeeds;

    public Runner(string id_, int speed_ , float[] speeds_)
    {
      _id = id_;
      _speed = speed_;
      _sectionSpeeds = speeds_;
    }

    public string Id
    {
      get { return _id; }
    }

    public int Speed
    {
      get { return _speed; }
    }

    public float[] SectionSpeeds
    {
      get { return _sectionSpeeds; }
    }
    public override string ToString()
    {
      return " [Runner: " + Id + "(" + Speed + " m/s)] ";
    }
  }
}
