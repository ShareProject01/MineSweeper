using UnityEngine;

public class PlayState : GameStateBase
{
    public enum WeaponType { Break,Flag };

    // ゲームプレイ中
	public override State GetState()
	{
		return State.Playing;
	}

    public WeaponType NowWeaponType { set; get; }

	// Start is called once before the first execution of Update after the MonoBehaviour is created
	protected override void Start()
    {
        base.Start();
        // 初期値設定
        NowWeaponType = WeaponType.Break;
    }

    // Update is called once per frame
    protected override void Update()
    {
        base.Update();
    }
}
