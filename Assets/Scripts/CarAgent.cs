using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using System.Collections.Generic;

namespace MLDemo
{
    public class CarAgent : Agent
    {
        [SerializeField]
        private Vector3 _spawnRange;

        [SerializeField]
        private Transform _goal;

        [SerializeField]
        private List<GameObject> _obstacles;

        [SerializeField] private float _speed = 10f;
        [SerializeField] private float _turnSpeed = 100f;
        [SerializeField] private float _simulationTimeScale = 20f;

        [SerializeField] private float _distanceReward;
        [SerializeField] private float _directionReward;
        [SerializeField] private float _speedReward;

        private Rigidbody _carRigidbody;
        private float _previousDistanceToGoal;
        private Vector3 _previousPosition;

        protected override void Awake()
        {
            base.Awake();
            _carRigidbody = GetComponent<Rigidbody>();
            if (_carRigidbody == null)
            {
                Debug.LogError("Rigidbody component is missing from the car agent.");
            }

            Time.timeScale = Academy.Instance.IsCommunicatorOn ? _simulationTimeScale : 1f;

            if (Academy.Instance.IsCommunicatorOn)
            {
                var mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    mainCamera.gameObject.SetActive(false);
                }
            }
        }

        public override void OnEpisodeBegin()
        {
            // Reset the car's position and velocity
            _carRigidbody.linearVelocity = Vector3.zero;
            _carRigidbody.angularVelocity = Vector3.zero;
            var stageX = _spawnRange.x;
            var stageZ = _spawnRange.z;
            Vector3 pos1, pos2;
            do
            {
                // Randomly position the car and goal within the stage bounds
                pos1 = new Vector3(Random.Range(stageX * -0.5f, stageX * 0.5f), 0.5f, Random.Range(stageZ * -0.5f, stageZ * 0.5f));
                pos2 = new Vector3(Random.Range(stageX * -0.5f, stageX * 0.5f), 0.5f, Random.Range(stageZ * -0.5f, stageZ * 0.5f));
            } while (Vector3.Distance(pos1, pos2) < _spawnRange.x * 0.25f); // Ensure car and goal are not too close

            foreach (var obstacle in _obstacles)
            {
                // Randomly position obstacles within the stage bounds
                Vector3 obstaclePos;
                do
                {
                    obstaclePos = new Vector3(Random.Range(stageX * -0.5f, stageX * 0.5f), 0.5f, Random.Range(stageZ * -0.5f, stageZ * 0.5f));
                } while (Vector3.Distance(obstaclePos, pos1) < obstacle.transform.lossyScale.x || Vector3.Distance(obstaclePos, pos2) < obstacle.transform.lossyScale.x);

                obstacle.transform.localPosition = obstaclePos;
                obstacle.SetActive(true);
            }

            transform.localPosition = pos1;
            transform.rotation = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up);

            _goal.localPosition = pos2;
            _previousDistanceToGoal = Vector3.Distance(transform.localPosition, _goal.localPosition);
            _previousPosition = transform.localPosition;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            var dirToGoal = (_goal.localPosition - transform.localPosition).normalized;
            sensor.AddObservation(dirToGoal.x);
            sensor.AddObservation(dirToGoal.z);

            sensor.AddObservation(_carRigidbody.linearVelocity.x / _speed);
            sensor.AddObservation(_carRigidbody.linearVelocity.z / _speed);

            float angleToGoal = Vector3.Angle(transform.forward, dirToGoal);
            sensor.AddObservation(angleToGoal / 180f); // 0～1に正規化
            // 【追加】ゴールとの距離を正規化して追加
            float distanceToGoal = Vector3.Distance(transform.localPosition, _goal.localPosition);
            // 20fはコースのおおよその最大距離。環境に合わせて調整
            sensor.AddObservation(distanceToGoal / _spawnRange.x);
            sensor.AddObservation(_carRigidbody.angularVelocity.y / _turnSpeed);

            // 後ろの壁までの距離を計算
            if (Physics.Raycast(transform.position, -transform.forward, out var hit, _spawnRange.z * 0.5f) &&
                (hit.collider.CompareTag("Wall") || hit.collider.CompareTag("Obstacle")))
            {
                // 後ろの壁までの距離を正規化して追加
                sensor.AddObservation(hit.distance / (_spawnRange.z * 0.5f));
            }
            else
            {
                // 壁が見つからない場合は最大距離を追加
                sensor.AddObservation(1f);
            }
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            // DISCRETE
            // // actionsから「離散的な」行動命令を取得
            // int moveAction = actions.DiscreteActions[0];

            // Vector3 moveDirection = Vector3.zero;
            // float turnDirection = 0f;

            // switch (moveAction)
            // {
            //     case 1: // 前進
            //         moveDirection = transform.forward;
            //         break;
            //     case 2: // 左折
            //         turnDirection = -1f;
            //         break;
            //     case 3: // 右折
            //         turnDirection = 1f;
            //         break;
            // }

            // // rigidbodyを使って車を動かす
            // _carRigidbody.MovePosition(_carRigidbody.position + moveDirection * _speed * Time.deltaTime);
            // transform.Rotate(0, turnDirection * _turnSpeed * Time.deltaTime, 0);

            // CONTINOUS
            // actionsから行動命令を取得
            float forward = actions.ContinuousActions[0]; // 前後 [-1, 1]
            float turn = actions.ContinuousActions[1]; // 左右 [-1, 1]

            // rigidbodyを使って車を動かす
            Vector3 moveDirection = transform.forward * forward * _speed * Time.deltaTime;
            _carRigidbody.MovePosition(_carRigidbody.position + moveDirection);
            _carRigidbody.MoveRotation(_carRigidbody.rotation * Quaternion.Euler(0, turn * _turnSpeed * Time.deltaTime, 0));

            // ゴールに近づいたら報酬
            float currentDistanceToGoal = Vector3.Distance(transform.localPosition, _goal.localPosition);
            float distanceReward = (_previousDistanceToGoal - currentDistanceToGoal) / _spawnRange.x * 5f;
            _previousDistanceToGoal = currentDistanceToGoal;

            // 【追加1】方向報酬（ゴール方向への進行度）
            Vector3 toGoal = (_goal.localPosition - transform.localPosition).normalized;
            float directionBonus = Vector3.Dot(transform.forward, toGoal) * 0.01f;

            // 【追加2】速度報酬（ゴール方向の速度成分）
            float velocityTowardGoal = Vector3.Dot(transform.localPosition - _previousPosition, toGoal);
            float speedBonus = Mathf.Clamp(velocityTowardGoal / _speed, -0.05f, 0.1f);

            // 総合報酬
            float totalReward = distanceReward;
            // float totalReward = distanceReward + directionBonus + speedBonus;
            AddReward(totalReward);

            // 新規：進行方向にRaycastでゴール検知
            if (Physics.Raycast(transform.position, transform.forward, out var hit, 30f))
            {
                if (hit.collider.CompareTag("Goal"))
                {
                    // Debug.DrawRay(transform.position, transform.forward * hit.distance, Color.green, 0.1f);
                    AddReward(0.001f); // ゴールが直線上に見える場合の報酬
                }
            }

            _distanceReward = distanceReward;
            _directionReward = directionBonus;
            _speedReward = speedBonus;

            // ゴールへの方向ベクトルを計算
            Vector3 goalDirection = (_goal.localPosition - transform.localPosition).normalized;
            // 車の前方ベクトルとゴール方向ベクトルの内積を計算
            float dotProduct = Vector3.Dot(transform.forward, goalDirection);

            // AIがゴールに背を向けているかチェック
            // dotProductが0未満（90度より大きい角度）の場合
            if (dotProduct < 0)
            {
                // 背を向けている角度がキツいほど、強いペナルティを与える
                // dotProductは-1に近づくほど真後ろを向いている
                // 例えば、真後ろを向いている時(-1)は -0.05 のペナルティ
                AddReward(dotProduct * 0.05f); 
            }
        }

        void FixedUpdate()
        {
            AddReward(-0.001f);
        }

        void OnCollisionEnter(Collision collision)
        {
            switch (collision.gameObject.tag)
            {
                case "Obstacle":
                case "Wall":
                    // 障害物に衝突した場合のペナルティ
                    AddReward(-5.0f);
                    EndEpisode();
                    break;
                default:
                    // その他の衝突は無視
                    break;
            }
        }

        void OnTriggerEnter(Collider other)
        {
            switch (other.gameObject.tag)
            {
                case "Goal":
                    // ゴールに到達した場合の報酬
                    AddReward(5.0f);
                    EndEpisode();
                    break;
                default:
                    // その他の衝突は無視
                    break;
            }
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            // DISCRETE
            // var discreteActionsOut = actionsOut.DiscreteActions;

            // // Wキーが押されていれば 1、そうでなければ Aキーをチェック...という風に繋げる
            // int action = Input.GetKey(KeyCode.W) ? 1 :
            //             Input.GetKey(KeyCode.A) ? 2 :
            //             Input.GetKey(KeyCode.D) ? 3 : 0; // どれも押されていなければ 0

            // discreteActionsOut[0] = action;

            // CONTINUOUS
            // ヒューリスティックモードでの操作
            var continuousActions = actionsOut.ContinuousActions;
            continuousActions[0] = Input.GetAxis("Vertical"); // 前後移動
            continuousActions[1] = Input.GetAxis("Horizontal"); // 左右回転
        }
    }
}
