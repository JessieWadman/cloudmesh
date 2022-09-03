import * as cdk from 'aws-cdk-lib';
import { RemovalPolicy } from 'aws-cdk-lib';
import { SubnetType, Vpc } from 'aws-cdk-lib/aws-ec2';
import { Cluster } from 'aws-cdk-lib/aws-ecs';
import { Effect, PolicyDocument, PolicyStatement, Role, ServicePrincipal } from 'aws-cdk-lib/aws-iam';
import { BlockPublicAccess, Bucket, BucketEncryption } from 'aws-cdk-lib/aws-s3';
import { NamespaceType } from 'aws-cdk-lib/aws-servicediscovery';
import { Queue } from 'aws-cdk-lib/aws-sqs';
import { DH_NOT_SUITABLE_GENERATOR } from 'constants';
import { Construct } from 'constructs';
import { CloudMesh } from './cloudmesh';
import { dotNetCode, DotNetLambda } from './dotnet-lambda';
import { DotNetTask, dotNetTaskCode } from './dotnet-task';

export class Example1Stack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const vpc = Vpc.fromLookup(this, "VPC", {
      isDefault: false,
      tags: {
        'aws:cloudformation:logical-id': 'vpc0'
      }
    });

    const cloudMesh = new CloudMesh(this, 'CloudMesh', {
      cloudMapNamespaceName: 'example1.cloudmesh',
      cloudMapNamespaceDescription: 'Example cloud mesh deployment',
      vpc
    });

    const demoQueue = new Queue(this, 'DemoQueue', { });
    cloudMesh.addQueue('demo', demoQueue);

    const storageBucket = new Bucket(this, 'SomeBlobs', {
      autoDeleteObjects: true,
      removalPolicy: RemovalPolicy.DESTROY,
      blockPublicAccess: BlockPublicAccess.BLOCK_ALL,
      encryption: BucketEncryption.S3_MANAGED
    });
    cloudMesh.addBucket('SomeBlobs', storageBucket);

    const orderService = new DotNetLambda(this, 'OrderService', {
      description: 'Order service',
      code: dotNetCode.buildProject('../src/LambdaCallerExample', 'bootstrap', 'x64')
    });
    cloudMesh.addLambdaService(orderService.handler, ['IFulfillmentService', 'IOrderPlacementService']);    

    const spawnTasksRole = new Role(this, 'ClusterSpawnTasksRole', {
        assumedBy: new ServicePrincipal('ecs-tasks.amazonaws.com'),
        inlinePolicies: {
            logging: new PolicyDocument({
              statements: [
                new PolicyStatement({
                  effect: Effect.ALLOW,
                  resources: ['*'],
                  actions: [ 'logs:*' ]
                })
              ]
            }),
            ecrRead: new PolicyDocument({
                statements: [
                    new PolicyStatement({
                        effect: Effect.ALLOW,
                        resources: ['*'],
                        actions: [
                            "ecr:GetAuthorizationToken",
                            "ecr:BatchCheckLayerAvailability",
                            "ecr:GetDownloadUrlForLayer",
                            "ecr:BatchGetImage"
                        ]
                    })
                ]
            })
        }
    });

    const ecsCluster = new Cluster(this, 'Cluster', {
      defaultCloudMapNamespace: {        
        name: cloudMesh.namespace.namespaceName,
        type: NamespaceType.DNS_PRIVATE,
        vpc: vpc
      },
      enableFargateCapacityProviders: true,
      vpc
    });

    const orderActors = new DotNetTask(this, 'CartService', {
      cloudMesh,
      code: dotNetTaskCode.buildProject('../src/EcsActorsExample', undefined, 'arm64'),
      description: 'CartService sample',
      ecsCluster,
      ecsClusterExecutionRole: spawnTasksRole,
      repositoryName: 'emample1/cart-service',
      actorInterfaceTypes: ['ICart', 'IProduct'],
      serviceInterfaceTypes: ['ICartService'],
      desiredCount: 1,
      maxHealthyPercent: 300,
      minHealthyPercent: 50
    });
  }
}
