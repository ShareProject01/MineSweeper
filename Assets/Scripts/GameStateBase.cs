using UnityEngine;

public abstract class GameStateBase : MonoBehaviour
{
	// ステータス情報の設定
	public enum State
	{
		Title,
		Playing,
		GameOver,
		Clear
	};
	
	// ステータス情報取得
	public abstract State GetState();

	protected virtual void Start()
	{

	}

	protected virtual void Update()
	{

	}
}
