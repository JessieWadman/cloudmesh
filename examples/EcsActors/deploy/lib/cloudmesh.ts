import { RemovalPolicy } from "aws-cdk-lib";
import { AttributeType, BillingMode, Table, TableEncryption } from "aws-cdk-lib/aws-dynamodb";
import { IVpc } from "aws-cdk-lib/aws-ec2";
import { Function } from "aws-cdk-lib/aws-lambda";
import { Bucket } from "aws-cdk-lib/aws-s3";
import { ISecret, Secret } from "aws-cdk-lib/aws-secretsmanager";
import { DiscoveryType, HttpNamespace, INamespace, NonIpInstance, PrivateDnsNamespace, Service } from "aws-cdk-lib/aws-servicediscovery";
import { IQueue } from "aws-cdk-lib/aws-sqs";
import { Construct } from "constructs";

export interface CloudMeshProps {
    cloudMapNamespaceName: string;
    cloudMapNamespaceDescription: string;
    vpc: IVpc;
}

export class CloudMesh extends Construct {

    public namespace: PrivateDnsNamespace;
    private actors: Service;
    private services: Service;
    private stores: Service;
    private queues: Service;
    private topics: Service;
    private secrets: Service;

    constructor(scope: Construct, id: string, props: CloudMeshProps) {
        super(scope, id);

        this.namespace = new PrivateDnsNamespace(this, 'CloudMapNamespace', {
            name: props.cloudMapNamespaceName,            
            description: props.cloudMapNamespaceDescription,            
            vpc: props.vpc
        });

        this.actors = new Service(this, 'Actors', {
            namespace: this.namespace,
            name: 'Actors',
            description: 'Actor type registrations',
            discoveryType: DiscoveryType.API
        });

        this.services = new Service(this, 'Services', {
            namespace: this.namespace,
            name: 'Services',
            description: 'Service type registrations',
            discoveryType: DiscoveryType.API
        });

        this.stores = new Service(this, 'Stores', {
            namespace: this.namespace,
            name: 'Stores',
            description: 'Store registrations',
            discoveryType: DiscoveryType.API
        });

        this.queues = new Service(this, 'Queues', {
            namespace: this.namespace,
            name: 'Queues',
            description: 'Queue registrations',
            discoveryType: DiscoveryType.API
        });

        this.topics = new Service(this, 'Topics', {
            namespace: this.namespace,
            name: 'Topics',
            description: 'Topic registrations',
            discoveryType: DiscoveryType.API
        });

        this.secrets = new Service(this, 'Secrets', {
            namespace: this.namespace,
            name: 'Secrets',
            description: 'Secrets registrations',
            discoveryType: DiscoveryType.API
        });

        const actorStateStore = new Table(this, 'ActorStates', {
            partitionKey: {
                name: 'Id',
                type: AttributeType.STRING
            },
            billingMode: BillingMode.PAY_PER_REQUEST,
            encryption: TableEncryption.AWS_MANAGED,
            pointInTimeRecovery: true,
            timeToLiveAttribute: 'Expires',
            removalPolicy: RemovalPolicy.DESTROY            
        });
        this.addTable('StateStore', actorStateStore);

        const singletonLeases = new Table(this, 'SingletonLeases', {
            partitionKey: {
                name: 'SingletonName',
                type: AttributeType.STRING
            },
            billingMode: BillingMode.PAY_PER_REQUEST,
            encryption: TableEncryption.AWS_MANAGED,
            pointInTimeRecovery: true,
            timeToLiveAttribute: 'Expires',
            removalPolicy: RemovalPolicy.DESTROY
        });
        this.addTable('SingletonLeases', singletonLeases);
    }

    public addQueue(name: string, queue: IQueue) {
        new NonIpInstance(this, `queue-${name}`, {
            service: this.queues,
            instanceId: name,
            customAttributes: {
                arn: queue.queueArn,
                fifo: queue.fifo ? "Yes" : "No",
                name: queue.queueName,
                url: queue.queueUrl
            }
        });
    }

    public addTable(name: string, table: Table) {
        new NonIpInstance(this, `table-${name}`, {
            service: this.stores,
            instanceId: name,
            customAttributes: {
                arn: table.tableArn,
                name: table.tableName
            }
        });
    }

    public addBucket(name: string, bucket: Bucket) {
        new NonIpInstance(this, `store-${name}`, {
            service: this.stores,
            instanceId: name,
            customAttributes: {
                arn: bucket.bucketArn,
                name: bucket.bucketName,
            }
        });
    }

    public addSecret(name: string, secret: ISecret) {
        new NonIpInstance(this, `secret-${name}`, {
            service: this.secrets,
            instanceId: name,
            customAttributes: {
                arn: secret.secretArn,
                name: secret.secretName
            }
        });
    }

    public addLambdaService(func: Function, serviceInterfaceTypes: string[]) {

        let timeout: number = -1;
        if (func.timeout) {
            timeout = func.timeout.toMilliseconds({ integral: true });
        }

        func.addEnvironment("cloudMapNamespace", this.namespace.namespaceName);

        for (const serviceInterfaceType of serviceInterfaceTypes) {
            new NonIpInstance(this, `service-${serviceInterfaceType}`, {
                service: this.services,
                instanceId: serviceInterfaceType,
                customAttributes: {
                    arn: func.functionArn,
                    name: func.functionName,
                    isBoundToVpc: func.isBoundToVpc ? 'Yes' : 'No',
                    timeout: timeout.toString()
                }
            });
        }
    }

    public addEcsServices(serviceName: string, serviceInterfaceTypes: string[]) {

        for (const serviceInterfaceType of serviceInterfaceTypes) {
            new NonIpInstance(this, `service-${serviceInterfaceType}`, {
                service: this.services,
                instanceId: serviceInterfaceType,
                customAttributes: {
                    serviceName: `service://${serviceName}`, // Service as in CloudMap service, not CloudMesh service
                }
            });
        }
    }

    public addEcsActors(serviceName: string, actorInterfaceTypes: string[]) {

        for (const serviceInterfaceType of actorInterfaceTypes) {
            new NonIpInstance(this, `actor-${serviceInterfaceType}`, {
                service: this.actors,
                instanceId: serviceInterfaceType,
                customAttributes: {
                    serviceName: `service://${serviceName}`, // Service as in CloudMap service, not CloudMesh service
                }
            });
        }
    }


}